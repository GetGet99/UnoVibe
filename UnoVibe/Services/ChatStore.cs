using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using UnoVibe.Models;

namespace UnoVibe.Services;

/// <summary>
/// Reactive store for the current chat session. Holds messages and applies SSE events
/// to them. All mutations happen on the UI thread via the dispatcher pump.
///
/// The mutable display fields are QuickMarkup reactive references (declared in the
/// markup header), so the pages bind to them directly. The references are created
/// lazily on first access, which must therefore happen on the UI thread for reactivity.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Models;
    public int HiddenMessages;
    public bool IsBusy;
    public int PendingPrompts;
    public string ConnectionStatus = "Connecting...";
    public string SessionTitle = "New Chat";
    public string UsageCostLabel = "$0.00";
    public string UsageTokensLabel = "0";
    public string ContextLabel = "0%";
    public double ContextUsage;
    public string ActiveSessionId = "";
    // Human-readable session status banner (busy/retry messages); empty means idle.
    public string StatusMessage = "";
    // The permission request currently shown to the user (oldest pending), or null.
    public PermissionRequestItem? ActivePermission;
    public string Mode = "build";
    public string ModelId = "";
    public string ProviderId = "";
    public string Variant = "Default";
    public bool HasVariants;
    public ToastItem? CurrentToast;
    """)]
public sealed partial class ChatStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonDefaults = new() { WriteIndented = false };

    /// <summary>Maximum number of messages kept in the UI; older ones are dropped to keep rendering smooth.</summary>
    public const int MaxVisibleMessages = 200;

    public ObservableCollection<string> ModeOptions { get; } = new();
    public ObservableCollection<ModelOption> ModelOptions { get; } = new();
    public ObservableCollection<string> VariantOptions { get; } = new();

    public ObservableCollection<MessageItem> Messages { get; } = new();

    private void AppendMessage(MessageItem message)
    {
        Messages.Add(message);
        while (Messages.Count > MaxVisibleMessages)
        {
            Messages.RemoveAt(0);
            HiddenMessages++;
        }
    }
    public ObservableCollection<SessionInfo> Sessions { get; } = new();
    public ObservableCollection<DirectoryGroup> DirectoryGroups { get; } = new();

    public string CurrentSessionId => _sessionId;

    /// <summary>Owns any locally-launched <c>opencode serve</c> process so it stays alive after navigation.</summary>
    public ServeProcess? ServeProcess { get; private set; }

    private OpencodeClient _client = null!;
    private readonly Channel<OpencodeEvent> _events = Channel.CreateUnbounded<OpencodeEvent>();
    private readonly Dictionary<string, MessageItem> _messagesById = new();
    private readonly Queue<string> _pendingPrompts = new();
    private readonly List<PermissionRequestItem> _permissions = new();
    private CancellationTokenSource? _cts;
    private DispatcherQueue? _dispatcher;
    private bool _started;
    private string _sessionId = "";
    private string? _pendingDirectory;
    private string _baseUrl = "";
    private string? _password;
    private string? _username;

    /// <summary>
    /// Configures the server to connect to. Must be called before <see cref="ConnectAsync"/>.
    /// </summary>
    public void Configure(string baseUrl, string? password = null, string? username = null)
    {
        baseUrl = baseUrl.Trim().TrimEnd('/');
        if (baseUrl.Length == 0 || (baseUrl == _baseUrl && password == _password && username == _username)) return;

        _baseUrl = baseUrl;
        _password = password;
        _username = username;
        _client = new OpencodeClient(baseUrl, password, username);
        _started = false;
        _cts?.Cancel();
        _cts = null;
        _sessionId = "";
        ActiveSessionId = "";
        SessionTitle = "New Chat";
        ResetUsageStats();
        IsBusy = false;
        StatusMessage = "";
        _permissions.Clear();
        ActivePermission = null;
        Messages.Clear();
        HiddenMessages = 0;
        _messagesById.Clear();
        Sessions.Clear();
        DirectoryGroups.Clear();
        ConnectionStatus = "Connecting...";
        ClearPendingPrompts();
        DismissToast();
    }

    /// <summary>
    /// Takes ownership of a locally-launched serve process. Disposes any previous one.
    /// </summary>
    public void AttachServeProcess(ServeProcess serve)
    {
        var old = ServeProcess;
        ServeProcess = serve;
        old?.Dispose();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts = null;
        _toastCts?.Cancel();
        _toastCts = null;
        ServeProcess?.Dispose();
        ServeProcess = null;
    }

    public async Task ConnectAsync()
    {
        if (_started) return;
        if (_client is null)
        {
            ConnectionStatus = "Error: no server configured";
            return;
        }
        _started = true;

        var cts = new CancellationTokenSource();
        _cts = cts;
        var ct = cts.Token;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _ = Task.Run(() => EventStreamReader.ReadAsync(_client.Http, $"{_client.BaseUrl}/event", _events.Writer, ct));
        _ = Task.Run(() => PumpAsync(ct));

        try
        {
            var healthy = await _client.HealthAsync(ct);
            if (healthy)
            {
                ConnectionStatus = "Connected";
            }
            else if (_client.LastHealthStatus == System.Net.HttpStatusCode.Unauthorized)
            {
                ConnectionStatus = "Error: unauthorized - check the server password";
            }
            else
            {
                ConnectionStatus = "Error: health check failed";
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
            return;
        }

        await RefreshSessionsAsync(ct);
        await RefreshSettingsAsync(ct);
    }

    public async Task SendAsync(string text)
    {
        try
        {
            if (!await EnsureSessionAsync()) return;

            // The server serializes `prompt_async` itself: if a turn is busy, the
            // message is stored immediately and the running session loop processes it
            // at the next agent step (after the in-flight tool call). So we always send
            // right away and defer ordering to the server — this matches the opencode TUI
            // (stream.transport.ts `runPromptTurn` fires promptAsync regardless of busy).
            //
            // TODO(settings/queuing): a future "queue on client" mode can route here
            // through EnqueuePrompt/DrainPendingPromptsAsync instead of sending now.
            await SendPromptNowAsync(text);
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Interrupts the currently-running turn (aborts in-flight tool calls and the
    /// model loop). Any queued prompts are flushed when the session goes idle.
    /// </summary>
    public async Task InterruptAsync()
    {
        if (_sessionId.Length == 0) return;
        try
        {
            await _client.AbortAsync(_sessionId);
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Renames the active session via PATCH /session/{id} and updates the local title.
    /// </summary>
    public async Task RenameSessionAsync(string title)
    {
        title = title.Trim();
        if (_sessionId.Length == 0 || title.Length == 0) return;
        try
        {
            await _client.UpdateSessionTitleAsync(_sessionId, title);
            SessionTitle = title;
            var session = Sessions.FirstOrDefault(s => s.Id == _sessionId);
            if (session is not null) session.Title = title;
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    // TODO(settings/queuing): client-side prompt queue, kept dormant for a future
    // "queue on client" mode. Currently unwired — SendAsync always sends immediately
    // and lets the server serialize prompts (see SendAsync comment). To enable a
    // client-owned queue, call EnqueuePrompt(text) from SendAsync when IsBusy, then
    // flush via DrainPendingPromptsAsync when the session goes idle.
    private void EnqueuePrompt(string text)
    {
        _pendingPrompts.Enqueue(text);
        PendingPrompts = _pendingPrompts.Count;
    }

    private void ClearPendingPrompts()
    {
        _pendingPrompts.Clear();
        PendingPrompts = 0;
    }

    private async Task SendPromptNowAsync(string text)
    {
        // Mark busy optimistically so interleaved SendAsync calls queue instead of
        // racing the HTTP call; the server's session.status busy event confirms it.
        IsBusy = true;
        await _client.SendPromptAsync(_sessionId, text, Mode, ProviderId, ModelId, Variant);
    }

    private bool _draining;

    /// <summary>Drains queued prompts one at a time. Called when the session goes idle.</summary>
    private async Task DrainPendingPromptsAsync()
    {
        if (_draining || IsBusy || _pendingPrompts.Count == 0) return;
        _draining = true;
        try
        {
            while (!IsBusy && _pendingPrompts.Count > 0)
            {
                var text = _pendingPrompts.Dequeue();
                PendingPrompts = _pendingPrompts.Count;
                try
                {
                    await SendPromptNowAsync(text);
                }
                catch (Exception ex)
                {
                    ConnectionStatus = $"Error: {ex.Message}";
                    return;
                }
            }
        }
        finally
        {
            _draining = false;
        }
    }

    private bool _creatingSession;

    private async Task<bool> EnsureSessionAsync()
    {
        if (_sessionId.Length > 0) return true;
        while (_creatingSession) await Task.Delay(10);
        if (_sessionId.Length > 0) return true;

        _creatingSession = true;
        try
        {
            // Lazy session creation: no title is passed (null) so the server assigns a
            // timestamped default title and auto-generates a name on the first prompt.
            _sessionId = await _client.CreateSessionAsync(null, _pendingDirectory, Mode, ProviderId, ModelId, Variant) ?? "";
            _pendingDirectory = null;
        }
        finally
        {
            _creatingSession = false;
        }

        if (_sessionId.Length == 0)
        {
            ConnectionStatus = "Error: could not create session";
            return false;
        }
        SessionTitle = "New Chat";
        ActiveSessionId = _sessionId;
        await RefreshSessionsAsync();
        return true;
    }

    public async Task RefreshSessionsAsync(CancellationToken ct = default)
    {
        try
        {
            var list = await _client.ListSessionsAsync(ct);
            Sessions.Clear();
            foreach (var session in list)
            {
                Sessions.Add(session);
            }

            RebuildDirectoryGroups();
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    /// <summary>Rebuilds the sidebar's directory grouping from <see cref="Sessions"/>.</summary>
    private void RebuildDirectoryGroups()
    {
        DirectoryGroups.Clear();
        foreach (var group in Sessions
            .GroupBy(s => s.Directory)
            .Select(g => new DirectoryGroup
            {
                Directory = g.Key.Length == 0 ? "(unknown)" : g.Key,
                Sessions = new ObservableCollection<SessionInfo>(g.OrderByDescending(s => s.Updated)),
            })
            .OrderByDescending(g => g.Sessions.Count > 0 ? g.Sessions[0].Updated : 0))
        {
            DirectoryGroups.Add(group);
        }
    }

    /// <summary>
    /// Applies a <c>session.created</c>/<c>session.updated</c> event. Keeps the sidebar and the active
    /// session header in sync when the server renames a session (e.g. the title agent replaces a
    /// default title with a generated one).
    /// </summary>
    private void ApplySessionUpsert(JsonElement properties)
    {
        if (!properties.TryGetProperty("info", out var info)) return;
        var session = SessionInfoFromJson(info);
        if (session.Id.Length == 0) return;

        var existing = Sessions.FirstOrDefault(s => s.Id == session.Id);
        if (existing is not null)
        {
            var directoryChanged = existing.Directory != session.Directory;
            existing.Title = session.Title;
            existing.Updated = session.Updated;
            existing.Agent = session.Agent;
            if (session.ModelId.Length > 0)
            {
                existing.ModelId = session.ModelId;
                existing.ModelProviderId = session.ModelProviderId;
                existing.ModelVariant = session.ModelVariant;
            }
            if (existing.Id == _sessionId) SessionTitle = existing.Title;

            // Title/Updated/Cost/tokens are QuickMarkup reactive fields on SessionInfo, so
            // mutating them propagates to the sidebar immediately. Only a directory change (which
            // moves the session between groups) requires rebuilding the sidebar groups.
            if (directoryChanged) RebuildDirectoryGroups();
        }
        else
        {
            Sessions.Add(session);
            RebuildDirectoryGroups();
        }
    }

    /// <summary>
    /// Applies a <c>session.deleted</c> event: removes the session from the sidebar
    /// immediately, and clears the active view if the deleted session was active.
    /// </summary>
    private void ApplySessionDeleted(JsonElement properties)
    {
        var id = properties.GetStringProperty("sessionID");
        if (id.Length == 0) return;

        var removed = Sessions.FirstOrDefault(s => s.Id == id);
        if (removed is null) return;

        Sessions.Remove(removed);
        RebuildDirectoryGroups();

        if (id != _sessionId) return;

        // The active session was deleted; fall back to an empty state.
        _sessionId = "";
        ActiveSessionId = "";
        SessionTitle = "New Chat";
        Messages.Clear();
        HiddenMessages = 0;
        _messagesById.Clear();
        ResetUsageStats();
        IsBusy = false;
        StatusMessage = "";
        ClearPendingPrompts();
        _permissions.Clear();
        ActivePermission = null;
        DismissToast();
    }

    private static SessionInfo SessionInfoFromJson(JsonElement item)
    {
        var info = new SessionInfo
        {
            Id = item.GetStringProperty("id"),
            Title = item.GetStringProperty("title"),
            Directory = item.GetStringProperty("directory"),
            ProjectId = item.GetStringProperty("projectID"),
            Path = item.GetStringProperty("path"),
            Agent = item.GetStringProperty("agent"),
        };
        if (item.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.Object)
        {
            info.ModelId = model.GetStringProperty("id");
            info.ModelProviderId = model.GetStringProperty("providerID");
            info.ModelVariant = model.GetStringProperty("variant");
        }
        if (item.TryGetProperty("time", out var time))
            info.Updated = time.TryGetProperty("updated", out var updated) ? updated.GetInt64() : 0;
        if (item.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Number)
            info.Cost = cost.GetDouble();
        if (item.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object)
        {
            info.TokensInput = tokens.GetInt64Property("input");
            info.TokensOutput = tokens.GetInt64Property("output");
            info.TokensReasoning = tokens.GetInt64Property("reasoning");
            if (tokens.TryGetProperty("cache", out var cache) && cache.ValueKind == JsonValueKind.Object)
            {
                info.TokensCacheRead = cache.GetInt64Property("read");
                info.TokensCacheWrite = cache.GetInt64Property("write");
            }
        }
        return info;
    }

    public async Task RefreshSettingsAsync(CancellationToken ct = default)
    {
        try
        {
            var modes = await _client.GetModesAsync(ct);
            ModeOptions.Clear();
            foreach (var mode in modes) ModeOptions.Add(mode);
            if (Mode.Length == 0 || !ModeOptions.Contains(Mode)) Mode = "build";

            var models = await _client.GetModelsAsync(ct);
            ModelOptions.Clear();
            foreach (var model in models) ModelOptions.Add(model);

            var known = Sessions.FirstOrDefault(s => s.ModelId.Length > 0 && ModelOptions.Any(m => m.Id == s.ModelId));
            if (known is not null)
            {
                ModelId = known.ModelId;
                ProviderId = known.ModelProviderId.Length > 0 ? known.ModelProviderId : ProviderId;
            }
            UpdateVariantOptions();
            ReapplyComboSelections();
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    private void ApplySessionSettings(SessionInfo session)
    {
        if (session.Agent.Length > 0) Mode = session.Agent;
        if (session.ModelId.Length > 0)
        {
            ModelId = session.ModelId;
            ProviderId = session.ModelProviderId;
        }
        UpdateVariantOptions();
        Variant = session.ModelVariant is "" or "default" ? "Default" : session.ModelVariant;
        ReapplyComboSelections();
    }

    // Reference.Value only fires when the value changes; the SelectedItem bindings ran once
    // against empty options, so nudge the refs to make the bindings re-apply the selection.
    private void ReapplyComboSelections()
    {
        var mode = Mode; Mode = ""; Mode = mode;
        var modelId = ModelId; ModelId = ""; ModelId = modelId;
        var variant = Variant; Variant = ""; Variant = variant;
    }

    private void UpdateVariantOptions()
    {
        VariantOptions.Clear();
        VariantOptions.Add("Default");
        var model = ModelOptions.FirstOrDefault(m => m.Id == ModelId && m.ProviderId == ProviderId);
        if (model is not null)
            foreach (var v in model.Variants) VariantOptions.Add(v);
        HasVariants = model?.Variants.Length > 0;
        if (Variant != "Default" && !VariantOptions.Contains(Variant)) Variant = "Default";
    }

    public void SetMode(string mode)
    {
        if (mode.Length > 0) Mode = mode;
    }

    public void SetModel(string modelId)
    {
        if (modelId.Length == 0 || modelId == ModelId) return;
        var model = ModelOptions.FirstOrDefault(m => m.Id == modelId);
        if (model is null) return;
        ModelId = model.Id;
        ProviderId = model.ProviderId;
        Variant = "Default";
        UpdateVariantOptions();
        ReapplyComboSelections();
    }

    public void SetVariant(string variant)
    {
        Variant = variant.Length == 0 ? "Default" : variant;
    }

    /// <summary>
    /// Starts a new (unsaved) chat. The session is created lazily on the first message send
    /// (<see cref="EnsureSessionAsync"/>), so clicking "+" doesn't produce an empty server-side
    /// session. <paramref name="directory"/> is remembered for that deferred creation.
    /// </summary>
    public Task NewSessionAsync(string? directory = null)
    {
        _pendingDirectory = directory;
        _sessionId = "";
        ActiveSessionId = "";
        SessionTitle = "New Chat";
        Messages.Clear();
        HiddenMessages = 0;
        _messagesById.Clear();
        ResetUsageStats();
        IsBusy = false;
        StatusMessage = "";
        ClearPendingPrompts();
        _permissions.Clear();
        ActivePermission = null;
        DismissToast();
        return Task.CompletedTask;
    }

    public async Task SwitchSessionAsync(string sessionId)
    {
        if (sessionId.Length == 0 || sessionId == _sessionId) return;
        _sessionId = sessionId;
        SessionTitle = Sessions.FirstOrDefault(s => s.Id == sessionId)?.Title ?? "Chat";
        ActiveSessionId = sessionId;
        Messages.Clear();
        HiddenMessages = 0;
        _messagesById.Clear();
        IsBusy = false;
        StatusMessage = "";
        ClearPendingPrompts();

        var known = Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (known is not null) ApplySessionSettings(known);

        try
        {
            var root = await _client.GetMessagesAsync(sessionId);
            if (root.ValueKind != JsonValueKind.Array) return;
            foreach (var msg in root.EnumerateArray())
            {
                var message = MessageFromJson(msg);
                if (message is null) continue;
                _messagesById[message.Id] = message;
                AppendMessage(message);
            }
            UpdateSessionStats();
            await SyncPendingQuestionsAsync();
            await SyncPendingPermissionsAsync();
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    private static MessageItem? MessageFromJson(JsonElement msg)
    {
        if (!msg.TryGetProperty("info", out var info) || info.GetStringProperty("id").Length == 0) return null;
        var item = new MessageItem
        {
            Id = info.GetStringProperty("id"),
            Role = info.GetStringProperty("role"),
            Agent = info.GetStringProperty("agent"),
        };
        ApplyMessageStats(item, info);
        if (msg.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.GetStringProperty("type") is "step-start" or "step-finish") continue;
                var p = PartFromJson(part);
                if (p.Type == "text" && p.Synthetic) continue;
                if (p.Id.Length > 0) item.Parts.Add(p);
            }
        }
        if (IsAbortedError(info) && item.Parts.All(p => p.Type != "aborted"))
        {
            item.Interrupted = true;
            item.Parts.Add(new PartItem { Id = $"aborted-{item.Id}", MessageId = item.Id, Type = "aborted" });
        }
        else
        {
            ApplyMessageError(item, info);
        }
        if (item.Role == "user" && item.Parts.Count == 0) return null;
        return item;
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        var reader = _events.Reader;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await reader.WaitToReadAsync(ct)) break;
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var batch = new List<OpencodeEvent>();
            while (reader.TryRead(out var evt)) batch.Add(evt);

            _dispatcher?.TryEnqueue(() =>
            {
                foreach (var evt in batch) Apply(evt);
            });
        }
    }

    private void Apply(OpencodeEvent evt)
    {
        // Message/status events are scoped to the active session. Session-level CRUD events
        // (created/updated/deleted) must NOT be filtered: they reflect the whole sidebar even
        // when they concern a session that isn't currently active (e.g. created/renamed/deleted
        // from another client).
        if (evt.Type is "message.updated" or "message.part.updated" or "message.part.delta"
            or "message.part.removed" or "message.removed" or "session.status" or "session.idle"
            or "session.error" or "session.diff" or "session.compacted" or "question.asked"
            or "question.replied" or "question.rejected")
        {
            var sessionId = evt.Properties.GetStringProperty("sessionID");
            if (sessionId.Length > 0 && sessionId != _sessionId) return;
        }

        switch (evt.Type)
        {
            case "message.updated":
                ApplyMessageUpdated(evt.Properties);
                break;
            case "message.part.updated":
                ApplyPartUpdated(evt.Properties);
                break;
            case "message.part.delta":
                ApplyPartDelta(evt.Properties);
                break;
            case "message.part.removed":
                ApplyPartRemoved(evt.Properties);
                break;
            case "session.status":
                ApplySessionStatus(evt.Properties);
                break;
            case "question.asked":
                ApplyQuestionAsked(evt.Properties);
                break;
            case "permission.asked":
                ApplyPermissionAsked(evt.Properties);
                break;
            case "permission.replied":
                ApplyPermissionReplied(evt.Properties);
                break;

            // -------------------------------------------------------------------
            // Events the opencode server /event stream emits but this client does
            // not act on yet. Tracked as future gaps (see AGENTS.md, "opencode
            // Server Integration"). Implement each and remove its TODO marker.
            // -------------------------------------------------------------------

            // Messages
            case "message.removed":
                // TODO: properties { sessionID, messageID }; drop the message from Messages/_messagesById.
                break;

            // Sessions
            case "session.created":
            case "session.updated":
                ApplySessionUpsert(evt.Properties);
                break;
            case "session.deleted":
                ApplySessionDeleted(evt.Properties);
                break;
            case "session.error":
                // TODO: properties { sessionID?, error }; surface server-side session errors.
                break;
            case "session.diff":
                // TODO: properties { sessionID, diff }; show file diffs produced by the session.
                break;
            case "session.idle":
                // TODO: properties { sessionID }; deprecated — superseded by session.status {type:"idle"}.
                break;
            case "session.compacted":
                // TODO: properties { sessionID }; mark the session as compacted.
                break;

            // Questions
            case "question.replied":
                // TODO: properties { sessionID, requestID, answers }; answered elsewhere — mark the form answered.
                break;
            case "question.rejected":
                // TODO: properties { sessionID, requestID }; question rejected — clear the pending form.
                break;

            // Files / project / VCS
            case "file.edited":
                // TODO: properties { file }; the agent edited a file on disk.
                break;
            case "file.watcher.updated":
                // TODO: properties { file, event: "add"|"change"|"unlink" }.
                break;
            case "vcs.branch.updated":
                // TODO: properties { branch }; the git branch changed in the workspace.
                break;
            case "todo.updated":
                // TODO: the todo list changed; the TUI renders it inline.
                break;
            case "lsp.updated":
                // TODO: LSP status changed; properties {}.
                break;

            // Tools / commands / MCP
            case "command.executed":
                // TODO: a custom command was executed server-side.
                break;
            case "mcp.tools.changed":
                // TODO: an MCP server's tool set changed.
                break;
            case "mcp.browser.open.failed":
                // TODO: an MCP browser-open attempt failed.
                break;

            // Server / stream control
            case "server.connected":
                // TODO: first event on the /event stream ({}); could drive connection state.
                break;
            case "server.heartbeat":
                // TODO: sent every 10s ({}) to keep the stream alive; ignoring is fine.
                break;
            case "server.instance.disposed":
                // TODO: the server instance was disposed ({}); the stream ends after this event.
                break;

            // TUI command plumbing (server → client commands; relevant only if adopting them)
            case "tui.toast.show":
                ApplyToastShow(evt.Properties);
                break;
        }
    }

    private CancellationTokenSource? _toastCts;

    private void ApplyToastShow(JsonElement properties)
    {
        var variant = properties.GetStringProperty("variant");
        var duration = properties.GetInt64Property("duration");
        ShowToast(new ToastItem
        {
            Title = properties.GetStringProperty("title"),
            Message = properties.GetStringProperty("message"),
            Variant = variant.Length > 0 ? variant : "info",
            DurationMs = duration > 0 ? (int)duration : 5000,
        });
    }

    /// <summary>Shows a toast, replacing any current one, and auto-dismisses it after <see cref="ToastItem.DurationMs"/>.</summary>
    public void ShowToast(ToastItem toast)
    {
        _toastCts?.Cancel();
        _toastCts = null;
        CurrentToast = toast;
        if (toast.DurationMs <= 0) return;

        var cts = new CancellationTokenSource();
        _toastCts = cts;
        _ = DismissToastAfterAsync(toast.DurationMs, cts.Token);
    }

    /// <summary>Immediately hides the current toast (clear any pending auto-dismiss).</summary>
    public void DismissToast()
    {
        _toastCts?.Cancel();
        _toastCts = null;
        CurrentToast = null;
    }

    private async Task DismissToastAfterAsync(int durationMs, CancellationToken ct)
    {
        try
        {
            await Task.Delay(durationMs, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (_dispatcher is null) { CurrentToast = null; return; }
        _dispatcher.TryEnqueue(() =>
        {
            if (ct.IsCancellationRequested) return;
            CurrentToast = null;
        });
    }

    private void ApplyMessageUpdated(JsonElement properties)
    {
        if (!properties.TryGetProperty("info", out var info)) return;
        var id = info.GetStringProperty("id");
        if (id.Length == 0) return;

        if (_messagesById.TryGetValue(id, out var message))
        {
            var role = info.GetStringProperty("role");
            if (role.Length > 0) message.Role = role;
            ApplyMessageStats(message, info);
            MarkInterrupted(message, info);
            ApplyMessageError(message, info);
            if (info.TryGetProperty("finish", out _)) OnTurnCompleted();
            UpdateSessionStats();
            return;
        }

        message = new MessageItem
        {
            Id = id,
            Role = info.GetStringProperty("role"),
            Agent = info.GetStringProperty("agent"),
        };
        ApplyMessageStats(message, info);
        MarkInterrupted(message, info);
        ApplyMessageError(message, info);
        if (info.TryGetProperty("finish", out _)) OnTurnCompleted();
        _messagesById[id] = message;
        AppendMessage(message);
        UpdateSessionStats();
    }

    /// <summary>Appends a reactive "aborted" marker part when the message carries an abort error.</summary>
    private static void MarkInterrupted(MessageItem message, JsonElement info)
    {
        if (!IsAbortedError(info)) return;
        message.Interrupted = true;
        if (message.Parts.Any(p => p.Type == "aborted")) return;
        message.Parts.Add(new PartItem
        {
            Id = $"aborted-{Guid.NewGuid():N}",
            MessageId = message.Id,
            Type = "aborted",
        });
    }

    private static bool IsAbortedError(JsonElement info)
    {
        if (!info.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object) return false;
        return error.GetStringProperty("name") == "MessageAbortedError";
    }

    /// <summary>
    /// Adds a reactive "error" part when the message carries a non-abort error (e.g. a
    /// streaming failure like <c>"Streaming response failed: [503] The request queue is full."</c>).
    /// Aborts are rendered via the interrupted path instead.
    /// </summary>
    private static void ApplyMessageError(MessageItem message, JsonElement info)
    {
        if (!info.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object) return;
        var name = error.GetStringProperty("name");
        if (name == "MessageAbortedError") return;
        if (message.Parts.Any(p => p.Type == "error")) return;

        message.Parts.Add(new PartItem
        {
            Id = $"error-{message.Id}-{Guid.NewGuid():N}",
            MessageId = message.Id,
            Type = "error",
            ErrorName = name,
            ErrorMessage = UnwrapErrorMessage(error),
        });
    }

    private static string UnwrapErrorMessage(JsonElement error)
    {
        string message = "";
        if (error.TryGetProperty("data", out var data))
        {
            if (data.ValueKind == JsonValueKind.String)
                message = data.GetString() ?? "";
            else if (data.ValueKind == JsonValueKind.Object)
                message = data.GetStringProperty("message");
        }

        message = message.Trim();
        if (message.Length >= 2 && message[0] == '"' && message[^1] == '"')
            message = message.Substring(1, message.Length - 2);
        return message;
    }

    /// <summary>Runs when the active turn ends (message finished or session idle).</summary>
    private void OnTurnCompleted()
    {
        IsBusy = false;
        _ = DrainPendingPromptsAsync();
    }

    private void ApplyPartUpdated(JsonElement properties)
    {
        if (!properties.TryGetProperty("part", out var part)) return;
        if (!_messagesById.TryGetValue(part.GetStringProperty("messageID"), out var message)) return;

        var partId = part.GetStringProperty("id");
        var existing = message.Parts.FirstOrDefault(p => p.Id == partId);
        if (existing is null)
        {
            if (part.GetStringProperty("type") is "step-start" or "step-finish") return;
            var p = PartFromJson(part);
            if (p.Synthetic && p.Type == "text" && message.Parts.Count == 0)
            {
                Messages.Remove(message);
                return;
            }
            message.Parts.Add(p);
            return;
        }

        UpdatePart(existing, part);
    }

    private void ApplyPartDelta(JsonElement properties)
    {
        var messageId = properties.GetStringProperty("messageID");
        var partId = properties.GetStringProperty("partID");
        var field = properties.GetStringProperty("field");
        var delta = properties.GetStringProperty("delta");
        if (field != "text" || delta.Length == 0) return;
        if (!_messagesById.TryGetValue(messageId, out var message)) return;

        var part = message.Parts.FirstOrDefault(p => p.Id == partId);
        if (part is null) return;
        part.Text += delta;
    }

    private void ApplyPartRemoved(JsonElement properties)
    {
        var messageId = properties.GetStringProperty("messageID");
        var partId = properties.GetStringProperty("partID");
        if (!_messagesById.TryGetValue(messageId, out var message)) return;

        var part = message.Parts.FirstOrDefault(p => p.Id == partId);
        if (part is not null) message.Parts.Remove(part);
    }

    private void ApplySessionStatus(JsonElement properties)
    {
        if (!properties.TryGetProperty("status", out var status)) return;
        var type = status.GetStringProperty("type");
        IsBusy = type != "idle";

        if (type == "retry")
        {
            var message = status.GetStringProperty("message");
            var attempt = status.GetInt64Property("attempt");
            var next = status.GetInt64Property("next");
            var prefix = attempt > 0 ? $"Retry #{attempt}" : "Retry";
            if (next > 0) prefix += $" ({next} left)";
            StatusMessage = message.Length > 0 ? $"{prefix}: {message}" : prefix;
        }
        else
        {
            StatusMessage = "";
        }

        if (!IsBusy) _ = DrainPendingPromptsAsync();
    }

    private void ApplyQuestionAsked(JsonElement properties)
    {
        var requestId = properties.GetStringProperty("id");
        if (requestId.Length == 0) return;
        if (!properties.TryGetProperty("tool", out var tool)) return;

        var messageId = tool.GetStringProperty("messageID");
        var callId = tool.GetStringProperty("callID");
        if (messageId.Length == 0 || callId.Length == 0) return;

        if (!_messagesById.TryGetValue(messageId, out var message)) return;
        var part = message.Parts.FirstOrDefault(p => p.CallId == callId);
        if (part is null) return;

        AttachQuestion(part, requestId, properties);
    }

    private static void AttachQuestion(PartItem part, string requestId, JsonElement properties)
    {
        part.QuestionRequestId = requestId;
        if (properties.TryGetProperty("questions", out var questions) && questions.ValueKind == JsonValueKind.Array)
        {
            part.QuestionJson = JsonSerializer.Serialize(questions, JsonDefaults);
            PopulateQuestionForm(part, questions);
        }
    }

    // Permission events are intentionally NOT filtered by session: subagents run in
    // their own sessions, and a pending subagent permission would otherwise hang forever.

    private void ApplyPermissionAsked(JsonElement properties)
    {
        var requestId = properties.GetStringProperty("id");
        if (requestId.Length == 0) return;
        AddPermissionRequest(PermissionRequestItem.FromJson(properties));
    }

    private void ApplyPermissionReplied(JsonElement properties)
    {
        var requestId = properties.GetStringProperty("requestID");
        if (requestId.Length > 0) RemovePermissionRequest(requestId);
    }

    public void AddPermissionRequest(PermissionRequestItem request)
    {
        if (_permissions.Any(p => p.Id == request.Id)) return;
        _permissions.Add(request);
        UpdateActivePermission();
    }

    public void RemovePermissionRequest(string requestId)
    {
        var index = _permissions.FindIndex(p => p.Id == requestId);
        if (index < 0) return;
        _permissions.RemoveAt(index);
        UpdateActivePermission();
    }

    private void UpdateActivePermission() => ActivePermission = _permissions.FirstOrDefault();

    /// <summary>Replies to a pending permission request and surfaces the next pending one, if any.</summary>
    public async Task ReplyPermissionAsync(string requestId, string reply, string? message = null)
    {
        try
        {
            await _client.ReplyPermissionAsync(requestId, reply, message);
            RemovePermissionRequest(requestId);
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Re-attaches pending question requestIDs to their tool parts after a session
    /// was reloaded from the server (the requestID is only present in the live
    /// question.asked event and the server's in-memory pending map, not in the
    /// persisted message parts).
    /// </summary>
    public async Task SyncPendingQuestionsAsync()
    {
        try
        {
            var root = await _client.GetPendingQuestionsAsync();
            if (root.ValueKind != JsonValueKind.Array) return;

            foreach (var question in root.EnumerateArray())
            {
                var sessionId = question.GetStringProperty("sessionID");
                if (sessionId.Length > 0 && sessionId != _sessionId) continue;
                if (!question.TryGetProperty("tool", out var tool)) continue;

                var messageId = tool.GetStringProperty("messageID");
                var callId = tool.GetStringProperty("callID");
                if (messageId.Length == 0 || callId.Length == 0) continue;
                if (!_messagesById.TryGetValue(messageId, out var message)) continue;

                var part = message.Parts.FirstOrDefault(p => p.CallId == callId && p.ToolName == "question");
                if (part is null || part.QuestionRequestId.Length > 0) continue;

                AttachQuestion(part, question.GetStringProperty("id"), question);
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Re-attaches pending permission requests after a session reload (requestIDs only
    /// exist in the live permission.asked event and the server's in-memory pending map).
    /// </summary>
    public async Task SyncPendingPermissionsAsync()
    {
        try
        {
            var root = await _client.GetPendingPermissionsAsync();
            if (root.ValueKind != JsonValueKind.Array) return;

            foreach (var request in root.EnumerateArray())
            {
                var id = request.GetStringProperty("id");
                if (id.Length == 0) continue;
                AddPermissionRequest(PermissionRequestItem.FromJson(request));
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    public async Task ReplyQuestionAsync(string requestId, IReadOnlyList<IReadOnlyList<string>> answers)
    {
        try
        {
            await _client.ReplyQuestionAsync(requestId, answers);
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    private static void PopulateQuestionForm(PartItem item, JsonElement questions)
    {
        item.QuestionForm.Clear();
        foreach (var q in questions.EnumerateArray())
        {
            var form = new QuestionFormItem
            {
                Question = q.GetStringProperty("question"),
                Header = q.GetStringProperty("header"),
                AllowCustom = q.GetBoolProperty("custom", true),
                Multiple = q.GetBoolProperty("multiple", false),
            };

            if (q.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
            {
                foreach (var opt in options.EnumerateArray())
                {
                    form.Options.Add(new QuestionOptionItem
                    {
                        Label = opt.GetStringProperty("label"),
                        Description = opt.GetStringProperty("description"),
                    });
                }
            }

            item.QuestionForm.Add(form);
        }
    }

    private static PartItem PartFromJson(JsonElement part)
    {
        var item = new PartItem
        {
            Id = part.GetStringProperty("id"),
            MessageId = part.GetStringProperty("messageID"),
            CallId = part.GetStringProperty("callID"),
            Type = part.GetStringProperty("type"),
        };

        if (item.Type is "text" or "reasoning" && part.TryGetProperty("text", out var text))
            item.Text = text.GetString() ?? "";

        item.Synthetic = part.GetBoolProperty("synthetic", false);

        if (item.Type == "reasoning" && part.TryGetProperty("time", out var time))
            item.Time = ParsePartTime(time);

        if (item.Type == "tool")
        {
            item.ToolName = part.GetStringProperty("tool");
            ApplyToolState(item, part);
        }

        if (item.Type == "file")
            item.FileName = part.GetStringProperty("filename") != "" ? part.GetStringProperty("filename") : part.GetStringProperty("url");

        if (part.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
            item.Files = files.EnumerateArray().Select(f => f.GetString() ?? "").Where(f => f.Length > 0).ToArray();

        return item;
    }

    private static void ApplyToolState(PartItem item, JsonElement part)
    {
        if (part.TryGetProperty("state", out var state))
        {
            item.ToolStatus = state.GetStringProperty("status");
            var title = state.GetStringProperty("title");
            if (title.Length > 0) item.ToolTitle = title;            if (state.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object)
            {
                var serialized = JsonSerializer.Serialize(input, JsonDefaults);
                if (serialized != "{}") item.ToolInput = serialized;
                if (input.TryGetProperty("command", out var command)) item.ToolCommand = command.GetString() ?? "";
                if (input.TryGetProperty("filePath", out var filePath)) item.ToolFilePath = filePath.GetString() ?? "";
                if (input.TryGetProperty("pattern", out var pattern)) item.ToolPattern = pattern.GetString() ?? "";
                if (input.TryGetProperty("path", out var searchPath)) item.ToolSearchPath = searchPath.GetString() ?? "";
                if (input.TryGetProperty("include", out var include)) item.ToolInclude = include.GetString() ?? "";
                if (input.TryGetProperty("workdir", out var workdir)) item.ToolWorkdir = workdir.GetString() ?? "";
                if (input.TryGetProperty("url", out var url)) item.ToolUrl = url.GetString() ?? "";
                if (input.TryGetProperty("name", out var skillName)) item.ToolSkillName = skillName.GetString() ?? "";
                if (input.TryGetProperty("todos", out var todos) && todos.ValueKind == JsonValueKind.Array)
                    item.TodoJson = JsonSerializer.Serialize(todos, JsonDefaults);
                if (input.TryGetProperty("questions", out var questions) && questions.ValueKind == JsonValueKind.Array)
                {
                    item.QuestionJson = JsonSerializer.Serialize(questions, JsonDefaults);
                    PopulateQuestionForm(item, questions);
                }
            }
            if (state.TryGetProperty("output", out var output))
                item.ToolOutput = output.GetString() ?? "";
            if (state.TryGetProperty("error", out var error))
                item.ToolError = error.GetString() ?? "";
            if (state.TryGetProperty("metadata", out var meta) && meta.ValueKind == JsonValueKind.Object)
            {
                if (meta.TryGetProperty("interrupted", out var interm)) item.Interrupted = interm.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.String => interm.GetString() == "true",
                    _ => item.Interrupted,
                };
                if (meta.TryGetProperty("output", out var mOutput)) item.ShellOutput = mOutput.GetString() ?? "";
                if (meta.TryGetProperty("diff", out var mDiff)) item.Diff = mDiff.GetString() ?? "";
                if (meta.TryGetProperty("count", out var mCount)) item.MatchCount = mCount.ToString();
                if (meta.TryGetProperty("matches", out var mMatches)) item.MatchCount = mMatches.ToString();
                if (meta.TryGetProperty("loaded", out var mLoaded) && mLoaded.ValueKind == JsonValueKind.Array)
                    item.LoadedFiles = string.Join("\n", mLoaded.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0));
                if (meta.TryGetProperty("todos", out var mTodos) && mTodos.ValueKind == JsonValueKind.Array)
                    item.TodoJson = JsonSerializer.Serialize(mTodos, JsonDefaults);
                if (meta.TryGetProperty("answers", out var mAnswers) && mAnswers.ValueKind == JsonValueKind.Array)
                    item.AnswerJson = JsonSerializer.Serialize(mAnswers, JsonDefaults);
            }
        }
        if (string.IsNullOrEmpty(item.ToolTitle)) item.ToolTitle = item.ToolName;
    }

    private static void ApplyMessageStats(MessageItem item, JsonElement info)
    {
        item.ModelId = info.GetStringProperty("modelID");
        item.ProviderId = info.GetStringProperty("providerID");
        if (info.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Number)
            item.Cost = cost.GetDouble();
        if (info.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object)
        {
            item.TokensInput = tokens.GetInt64Property("input");
            item.TokensOutput = tokens.GetInt64Property("output");
            item.TokensReasoning = tokens.GetInt64Property("reasoning");
            if (tokens.TryGetProperty("cache", out var cache) && cache.ValueKind == JsonValueKind.Object)
            {
                item.TokensCacheRead = cache.GetInt64Property("read");
                item.TokensCacheWrite = cache.GetInt64Property("write");
            }
        }
    }

    private void ResetUsageStats()
    {
        UsageCostLabel = "$0.00";
        UsageTokensLabel = "0";
        ContextLabel = "0%";
        ContextUsage = 0;
    }

    private void UpdateSessionStats()
    {
        var last = Messages.LastOrDefault(m => m.Role == "assistant" && m.TokensOutput > 0);
        if (last is null)
        {
            ResetUsageStats();
            return;
        }

        UsageCostLabel = FormatCost(last.Cost);

        var tokens = last.TokensInput + last.TokensOutput + last.TokensReasoning
            + last.TokensCacheRead + last.TokensCacheWrite;
        UsageTokensLabel = tokens.ToString("N0");

        var limit = ResolveContextLimit(last);
        if (limit > 0)
        {
            var percent = (int)Math.Round(tokens / (double)limit * 100);
            ContextLabel = $"{percent}%";
            ContextUsage = percent;
        }
        else
        {
            ContextLabel = "--";
            ContextUsage = 0;
        }
    }

    private long ResolveContextLimit(MessageItem message)
    {
        var model = ModelOptions.FirstOrDefault(m => m.Id == message.ModelId
            && (message.ProviderId.Length == 0 || m.ProviderId == message.ProviderId));
        model ??= ModelOptions.FirstOrDefault(m => m.Id == ModelId && m.ProviderId == ProviderId);
        return model?.LimitContext ?? 0;
    }

    private static string FormatCost(double cost)
    {
        if (cost <= 0) return "$0.00";
        if (cost < 0.01) return $"${cost:0.####}";
        return $"${cost:F2}";
    }

    private static ReasoningTime ParsePartTime(JsonElement time)
    {
        return new ReasoningTime
        {
            Start = time.GetInt64Property("start"),
            End = time.GetInt64Property("end"),
        };
    }

    private static void UpdatePart(PartItem item, JsonElement part)
    {
        if (item.Type is "text" or "reasoning" && part.TryGetProperty("text", out var text))
            item.Text = text.GetString() ?? "";

        if (item.Type == "reasoning" && part.TryGetProperty("time", out var time))
            item.Time = ParsePartTime(time);

        if (item.Type == "reasoning" || item.Type == "text")
            item.Synthetic = part.GetBoolProperty("synthetic", item.Synthetic);

        if (item.Type == "tool")
            ApplyToolState(item, part);

        if (item.Type == "file")
            item.FileName = part.GetStringProperty("filename") != "" ? part.GetStringProperty("filename") : part.GetStringProperty("url");
    }
}

file static class JsonElementExtensions
{
    public static string GetStringProperty(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var prop) ? prop.GetString() ?? "" : "";

    public static long GetInt64Property(this JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop)) return 0;
        if (prop.ValueKind == JsonValueKind.Number) return prop.GetInt64();
        return 0;
    }

    public static bool GetBoolProperty(this JsonElement element, string name, bool fallback)
    {
        if (!element.TryGetProperty(name, out var prop)) return fallback;
        if (prop.ValueKind == JsonValueKind.True) return true;
        if (prop.ValueKind == JsonValueKind.False) return false;
        if (prop.ValueKind == JsonValueKind.String && prop.GetString() == "true") return true;
        return fallback;
    }
}
