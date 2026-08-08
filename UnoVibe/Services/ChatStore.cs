using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
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
    public int PendingImageCount;
    public string ConnectionStatus = "Connecting...";
    public string SessionTitle = "New Chat";
    public string UsageCostLabel = "$0.00";
    public string UsageTokensLabel = "0";
    public string ContextLabel = "0%";
    public double ContextUsage;
    public long UsageTokensInput;
    public long UsageTokensOutput;
    public long UsageTokensReasoning;
    public long UsageTokensCacheRead;
    public long UsageTokensCacheWrite;
    public long ContextLimit;
    public string ActiveSessionId = "";
    // Parent session id when the active session is a subagent (spawned by a task tool call).
    // Empty for root sessions; drives the header's "back to parent" button.
    public string ParentSessionId = "";
    // Number of subagent sessions belonging to the active session (ParentId == _sessionId).
    // Drives the chat page's subagent strip and the flyout's "Tokens (excludes subagents)" label.
    public int SubagentCount;
    // Human-readable session status banner (busy/retry messages); empty means idle.
    public string StatusMessage = "";
    // Auto-retry state for the active turn (session.status type "retry"); drives the
    // end-of-chat retry card. RetryNextMs is the absolute unix-ms time of the next attempt.
    public bool IsRetrying;
    public string RetryMessage = "";
    public int RetryAttempt;
    public long RetryNextMs;
    // Live countdown text recomputed each second by the chat page timer ("Attempt #2 · retrying in 3s").
    public string RetryCountdown = "";
    // True when the active turn stopped because of a non-interrupt error; drives the "Continue" button.
    public bool TurnStoppedWithError;
    // The permission request currently shown to the user (oldest pending), or null.
    public PermissionRequestItem? ActivePermission;
    public string Mode = "build";
    public string ModelId = "";
    public string ProviderId = "";
    public string Variant = "Default";
    public bool HasVariants;
    public ToastItem? CurrentToast;
    // MCP servers for the active session's directory, in sidebar display order.
    public string McpDirectory = "";
    // Compact "N active, M inactive, K error" summary for the collapsed MCP sidebar header.
    // inactive = explicitly disabled; error = failed/needs_auth/needs_client_registration (mutually exclusive).
    public string McpSummary = "";
    // Undo marker for the active session: the id of the user message the conversation is
    // reverted to (the server's session "revert" field). Empty = not reverted. Drives the
    // revert card + message filter (messages with id >= RevertMessageId are hidden).
    public string RevertMessageId = "";
    // Card label for the revert banner, e.g. "1 message reverted". Computed whenever the
    // revert point changes (recounts the reverted user messages from the message list).
    public string RevertCountLabel = "";
    """)]
public sealed partial class ChatStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonDefaults = new() { WriteIndented = false };

    /// <summary>Maximum number of messages kept in the UI; older ones are dropped to keep rendering smooth.</summary>
    public const int MaxVisibleMessages = 200;

    /// <summary>Image file extensions accepted by the picker and the clipboard uri-list paste path.</summary>
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

    /// <summary>Image MIME types tried when pasting raw image bytes from the clipboard.</summary>
    private static readonly string[] ImageMimeTypes = { "image/png", "image/jpeg", "image/gif", "image/webp", "image/bmp" };

    public ObservableCollection<string> ModeOptions { get; } = new();
    public ObservableCollection<ModelOption> ModelOptions { get; } = new();
    public ObservableCollection<string> VariantOptions { get; } = new();

    public ObservableCollection<MessageItem> Messages { get; } = new();

    /// <summary>Image attachments staged for the next prompt (shown as thumbnails above the input).</summary>
    public ObservableCollection<ImageAttachment> PendingImages { get; } = new();

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
    // Subagent sessions (ParentId == active session id), in display order for the chat page
    // subagent strip. Rebuilt via RebuildActiveSubagents whenever the session list or the
    // active session changes.
    public ObservableCollection<SessionInfo> ActiveSubagents { get; } = new();

    public ObservableCollection<McpServerItem> McpServers { get; } = new();
    // Directory the McpServers collection currently reflects ("" = server default).
    private string _mcpDirectory = "";
    // Guards concurrent connect/disconnect requests (one toggle at a time).
    private bool _mcpBusy;
    // Background poll is only active while the sidebar MCP section is expanded.
    private volatile bool _mcpPolling;
    private const int McpPollIntervalMs = 5000;

    public string CurrentSessionId => _sessionId;

    /// <summary>
    /// The user prompt text of the message just undone, restored into the composer by the
    /// chat page after an undo (TUI parity). Plain field — read from code, not markup.
    /// </summary>
    public string RevertPromptText { get; private set; } = "";

    /// <summary>The HTTP client for the configured server (null before <see cref="Configure"/>).</summary>
    public OpencodeClient? Client => _client;

    /// <summary>Owns any locally-launched <c>opencode serve</c> process so it stays alive after navigation.</summary>
    public ServeProcess? ServeProcess { get; private set; }

    private OpencodeClient _client = null!;
    private readonly Channel<OpencodeEvent> _events = Channel.CreateUnbounded<OpencodeEvent>();
    private readonly Dictionary<string, MessageItem> _messagesById = new();
    // Per-session busy state keyed by session id; survives sidebar list rebuilds (RefreshSessionsAsync).
    private readonly Dictionary<string, string> _sessionStatus = new();
    // Per-session "completed but not viewed yet" state keyed by session id.
    private readonly Dictionary<string, bool> _unread = new();
    // Per-session last-turn outcome (""/success/error/interrupted), derived from the final
    // assistant message's info.error; drives the sidebar icon for unread sessions.
    private readonly Dictionary<string, string> _sessionOutcome = new();
    // Per-session counts of pending questions (question.asked not yet replied/rejected).
    private readonly Dictionary<string, int> _pendingQuestions = new();
    // Per-session counts of pending permission approvals (permission.asked not yet replied).
    private readonly Dictionary<string, int> _pendingPermissions = new();
    // Per-directory sidebar "show all sessions" state (keyed by directory), preserved across
    // sidebar group rebuilds so the show-more/show-less toggle survives session list refreshes.
    private readonly Dictionary<string, bool> _directoryExpanded = new();
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
        ParentSessionId = "";
        SessionTitle = "New Chat";
        ResetUsageStats();
        IsBusy = false;
        StatusMessage = "";
        ResetTurnFlags();
        ResetRevertState();
        _permissions.Clear();
        ActivePermission = null;
        Messages.Clear();
        HiddenMessages = 0;
        _messagesById.Clear();
        ActiveSubagents.Clear();
        SubagentCount = 0;
        Sessions.Clear();
        DirectoryGroups.Clear();
        _sessionStatus.Clear();
        _unread.Clear();
        _sessionOutcome.Clear();
        _pendingQuestions.Clear();
        _pendingPermissions.Clear();
        _directoryExpanded.Clear();
        McpServers.Clear();
        _mcpDirectory = "";
        McpDirectory = "";
        McpSummary = "";
        _mcpBusy = false;
        _mcpPolling = false;
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
        _ = Task.Run(() => McpPollLoopAsync(ct));

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
        await RefreshSessionStatusAsync(ct);
        await SyncPendingPermissionsAsync();
        await SyncPendingQuestionsAsync();
        await RefreshSettingsAsync(ct);
        await RefreshMcpStatusAsync(ct);
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
    /// Opens the native file picker and stages the chosen image as a pending attachment.
    /// </summary>
    public async Task PickImageAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
        };
        foreach (var ext in ImageExtensions)
            picker.FileTypeFilter.Add(ext);
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        await AddPendingImageAsync(file.Path);
    }

    /// <summary>Reads an image file from disk and stages it as a pending attachment.</summary>
    public async Task AddPendingImageAsync(string path)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            if (bytes.Length == 0) return;
            StageAttachment(new ImageAttachment
            {
                FileName = Path.GetFileName(path),
                Mime = ImageAttachment.MimeFromPath(path),
                Bytes = bytes,
                Preview = await ImageAttachment.DecodeAsync(bytes),
            });
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Pastes an image from the system clipboard (Ctrl+V). Returns true when at least one
    /// image was staged; false when the clipboard holds no usable image, so the caller can
    /// let the default text paste proceed.
    /// </summary>
    /// <remarks>
    /// Uses Uno's built-in <see cref="Clipboard"/> which on Skia/X11 routes to the
    /// <c>X11ClipboardExtension</c> (supports raw <c>image/png</c>/<c>image/jpeg</c> atoms and
    /// <c>text/uri-list</c>). Only the read path is needed here; the write path workaround from
    /// PocketPic is not required for pasting.
    /// </remarks>
    public async Task<bool> PasteImageFromClipboardAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content is null) return false;

            foreach (var mime in ImageMimeTypes)
            {
                if (!content.Contains(mime)) continue;
                var item = await content.GetDataAsync(mime);
                byte[]? bytes = item switch
                {
                    byte[] raw => raw,
                    IRandomAccessStream stream => await ReadAllBytes(stream),
                    _ => null,
                };
                if (bytes is { Length: > 0 })
                {
                    var ext = mime[(mime.LastIndexOf('/') + 1)..];
                    StageAttachment(await ImageAttachment.CreateFromBytesAsync(bytes, mime, $"Pasted image.{ext}"));
                    return true;
                }
            }

            if (content.Contains("text/uri-list"))
            {
                var items = await content.GetStorageItemsAsync();
                var staged = false;
                foreach (var item in items)
                {
                    if (item is not StorageFile file) continue;
                    var ext = Path.GetExtension(file.Path).ToLowerInvariant();
                    if (ImageExtensions.Contains(ext))
                    {
                        await AddPendingImageAsync(file.Path);
                        staged = true;
                    }
                }
                if (staged) return true;
            }
        }
        catch
        {
            // Foreign clipboard formats or an unavailable selection should not crash paste.
        }
        return false;
    }

    private static async Task<byte[]> ReadAllBytes(IRandomAccessStream stream)
    {
        stream.Seek(0);
        using var reader = new DataReader(stream);
        var size = (uint)stream.Size;
        await reader.LoadAsync(size);
        var bytes = new byte[size];
        reader.ReadBytes(bytes);
        return bytes;
    }

    private void StageAttachment(ImageAttachment attachment)
    {
        PendingImages.Add(attachment);
        PendingImageCount = PendingImages.Count;
    }

    /// <summary>Removes a staged image attachment.</summary>
    public void RemovePendingImage(ImageAttachment attachment)
    {
        PendingImages.Remove(attachment);
        PendingImageCount = PendingImages.Count;
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
        ResetTurnFlags();
        var images = PendingImages.ToArray();
        await _client.SendPromptAsync(_sessionId, text, images, Mode, ProviderId, ModelId, Variant);
        // Attachments travel with the prompt, so stage them off once the message is stored.
        PendingImages.Clear();
        PendingImageCount = 0;
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
        ParentSessionId = "";
        await RefreshSessionsAsync();
        await RefreshMcpStatusAsync();
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
                ApplySessionFlags(session);
                Sessions.Add(session);
            }

            RebuildDirectoryGroups();
            RebuildActiveSubagents();
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Polls GET /session/status for the currently-busy sessions. The server only emits
    /// session.status SSE events on transitions, so a session already mid-turn before we
    /// connected would otherwise never show as busy.
    /// </summary>
    public async Task RefreshSessionStatusAsync(CancellationToken ct = default)
    {
        try
        {
            _sessionStatus.Clear();
            foreach (var kv in await _client.GetSessionStatusAsync(ct)) _sessionStatus[kv.Key] = kv.Value;
            foreach (var s in Sessions) ApplySessionFlags(s);
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Refreshes the MCP server list from GET /mcp for the active session's directory.
    /// MCP status is per workspace directory (instance), not per session, so the sidebar
    /// reflects whichever session is currently open. When there is no session yet, falls
    /// back to the pending/current directory.
    /// </summary>
    public async Task RefreshMcpStatusAsync(CancellationToken ct = default)
    {
        var directory = ActiveDirectory();
        try
        {
            var status = await _client.GetMcpStatusAsync(directory, ct);
            McpServers.Clear();
            foreach (var (name, info) in status.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                McpServers.Add(new McpServerItem
                {
                    Name = name,
                    Status = info.Status,
                    Error = info.Error,
                });
            }
            _mcpDirectory = directory;
            McpDirectory = directory.Length > 0 ? directory : "(default)";
            var connected = status.Values.Count(s => s.Status == "connected");
            var inactive = status.Values.Count(s => s.Status == "disabled");
            var bad = status.Values.Count(s => s.Status is "failed" or "needs_auth" or "needs_client_registration");
            var summaryParts = new List<string>();
            if (connected > 0) summaryParts.Add($"{connected} active");
            if (inactive > 0) summaryParts.Add($"{inactive} inactive");
            if (bad > 0) summaryParts.Add($"{bad} error");
            McpSummary = summaryParts.Count > 0 ? string.Join(", ", summaryParts) : "none";
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Connects or disconnects an MCP server based on its current status, then refreshes
    /// the list. Mirrors the TUI: connected → disconnect, anything else → connect.
    /// </summary>
    public async Task ToggleMcpAsync(string name)
    {
        if (_mcpBusy) return;
        var server = McpServers.FirstOrDefault(s => s.Name == name);
        if (server is null) return;
        _mcpBusy = true;
        server.Connecting = true;
        var directory = _mcpDirectory;
        try
        {
            if (server.IsConnected) await _client.McpDisconnectAsync(name, directory);
            else await _client.McpConnectAsync(name, directory);
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
        finally
        {
            server.Connecting = false;
            _mcpBusy = false;
        }
        await RefreshMcpStatusAsync();
    }

    /// <summary>
    /// Turns the background MCP status poll on or off. The sidebar keeps it on only while
    /// the MCP section is expanded; the expand action also polls once immediately.
    /// </summary>
    public void SetMcpPolling(bool active) => _mcpPolling = active;

    /// <summary>
    /// Background poll: re-fetches GET /mcp every few seconds while enabled. The server
    /// pushes no MCP status event (only mcp.tools.changed, without status), so expanded
    /// sections need periodic polling to stay live. Runs on a background thread and hops
    /// to the UI dispatcher for the actual refresh, since McpServers/McpSummary are
    /// reactive references.
    /// </summary>
    private async Task McpPollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(McpPollIntervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            if (!_mcpPolling || _dispatcher is null) continue;
            _dispatcher.TryEnqueue(() => _ = RefreshMcpStatusAsync(ct));
        }
    }

    /// <summary>The directory used for instance-scoped MCP queries: the active session's, else the pending/current one.</summary>
    public string ActiveDirectory()
    {
        if (_sessionId.Length > 0)
        {
            var session = Sessions.FirstOrDefault(s => s.Id == _sessionId);
            if (session is not null && session.Directory.Length > 0) return session.Directory;
        }
        return _pendingDirectory ?? "";
    }

    /// <summary>Copies the reactive busy/unread/outcome/attention flags from the store's per-session maps onto a session item.</summary>
    private void ApplySessionFlags(SessionInfo session)
    {
        session.IsBusy = _sessionStatus.GetValueOrDefault(session.Id) is not (null or "idle");
        session.IsUnread = _unread.GetValueOrDefault(session.Id);
        session.Outcome = _sessionOutcome.GetValueOrDefault(session.Id) ?? "";
        session.NeedsAttention = SessionNeedsAttention(session.Id);
        session.AttentionKind = _pendingPermissions.GetValueOrDefault(session.Id) > 0 ? "permission"
            : _pendingQuestions.GetValueOrDefault(session.Id) > 0 ? "question"
            : "";
    }

    private bool SessionNeedsAttention(string sessionId) =>
        _pendingQuestions.GetValueOrDefault(sessionId) > 0 || _pendingPermissions.GetValueOrDefault(sessionId) > 0;

    /// <summary>Re-applies the reactive per-session flags to every sidebar item (after counters change).</summary>
    private void RefreshSessionFlags()
    {
        foreach (var s in Sessions) ApplySessionFlags(s);
    }

    /// <summary>
    /// Rebuilds the sidebar's directory grouping from <see cref="Sessions"/>. Subagent sessions
    /// (those spawned by a <c>task</c> tool call, identified by a non-empty ParentId) are kept in
    /// <see cref="Sessions"/> but filtered out of the sidebar — they're opened via their clickable
    /// task tool card instead, mirroring the TUI which hides them from session lists.
    /// </summary>
    private void RebuildDirectoryGroups()
    {
        DirectoryGroups.Clear();
        foreach (var group in Sessions
            .Where(s => !s.IsSubagent)
            .GroupBy(s => s.Directory)
            .Select(g => new DirectoryGroup
            {
                Directory = g.Key.Length == 0 ? "(unknown)" : g.Key,
                Sessions = new ObservableCollection<SessionInfo>(g.OrderByDescending(s => s.Updated)),
            })
            .OrderByDescending(g => g.Sessions.Count > 0 ? g.Sessions[0].Updated : 0))
        {
            // Re-apply the user's show-more/show-less choice; the toggle mutates the item's
            // reactive IsExpanded in place, while a rebuild gets its value from the store map.
            group.IsExpanded = _directoryExpanded.GetValueOrDefault(group.Directory);
            DirectoryGroups.Add(group);
        }
    }

    /// <summary>
    /// Expands/collapses a sidebar directory group (show all sessions vs. a capped preview).
    /// </summary>
    public void ToggleDirectoryExpanded(string directory)
    {
        var expanded = !_directoryExpanded.GetValueOrDefault(directory);
        _directoryExpanded[directory] = expanded;
        var group = DirectoryGroups.FirstOrDefault(g => g.Directory == directory);
        if (group is not null) group.IsExpanded = expanded;
    }

    /// <summary>
    /// Rebuilds <see cref="ActiveSubagents"/> (subagent sessions whose parent is the active
    /// session) and updates the reactive <see cref="SubagentCount"/>. Subagents are hidden from
    /// the sidebar, so this collection is the chat page's way to list them.
    /// </summary>
    private void RebuildActiveSubagents()
    {
        ActiveSubagents.Clear();
        if (_sessionId.Length == 0)
        {
            SubagentCount = 0;
            return;
        }
        foreach (var session in Sessions
            .Where(s => s.ParentId == _sessionId)
            .OrderByDescending(s => s.Updated))
        {
            ActiveSubagents.Add(session);
        }
        SubagentCount = ActiveSubagents.Count;
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
            RebuildActiveSubagents();
        }
        else
        {
            ApplySessionFlags(session);
            Sessions.Add(session);
            RebuildDirectoryGroups();
            RebuildActiveSubagents();
        }

        // Sync the undo marker for the active session. The server's session info carries a
        // "revert" object ({ messageID, partID?, snapshot?, diff? }) when reverted and omits the
        // field entirely on unrevert (patch() serializes revert:null to undefined). The chat page
        // hides messages with id >= the revert point via RevertMessageId. This also covers the
        // case where a revert is staged from another client or cleared by a new prompt's cleanup.
        if (session.Id == _sessionId)
        {
            var revertMessageId = "";
            if (info.TryGetProperty("revert", out var revert) && revert.ValueKind == JsonValueKind.Object)
                revertMessageId = revert.GetStringProperty("messageID");
            if (revertMessageId != RevertMessageId)
            {
                if (revertMessageId.Length == 0) RevertPromptText = "";
                ApplyRevertMarker(revertMessageId);
            }
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
        RebuildActiveSubagents();
        _sessionStatus.Remove(id);
        _unread.Remove(id);
        _sessionOutcome.Remove(id);
        _pendingQuestions.Remove(id);
        _pendingPermissions.Remove(id);

        if (id != _sessionId) return;

        // The active session was deleted; fall back to an empty state.
        _sessionId = "";
        ActiveSessionId = "";
        ParentSessionId = "";
        SessionTitle = "New Chat";
        Messages.Clear();
        HiddenMessages = 0;
        _messagesById.Clear();
        ResetUsageStats();
        IsBusy = false;
        StatusMessage = "";
        ResetTurnFlags();
        ResetRevertState();
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
            ParentId = item.GetStringProperty("parentID"),
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

            // Prefer a root session (not a subagent) when guessing the model for new chats.
            var known = Sessions.FirstOrDefault(s => !s.IsSubagent && s.ModelId.Length > 0 && ModelOptions.Any(m => m.Id == s.ModelId));
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
        ParentSessionId = "";
        SessionTitle = "New Chat";
        Messages.Clear();
        HiddenMessages = 0;
        _messagesById.Clear();
        ResetUsageStats();
        IsBusy = false;
        StatusMessage = "";
        ResetTurnFlags();
        ResetRevertState();
        ClearPendingPrompts();
        RebuildActiveSubagents();
        _permissions.Clear();
        ActivePermission = null;
        DismissToast();
        _ = RefreshMcpStatusAsync();
        return Task.CompletedTask;
    }

    public async Task SwitchSessionAsync(string sessionId)
    {
        if (sessionId.Length == 0 || sessionId == _sessionId) return;
        _sessionId = sessionId;
        var known = Sessions.FirstOrDefault(s => s.Id == sessionId);
        ParentSessionId = known?.ParentId ?? "";
        SessionTitle = known?.Title.Length > 0 ? known.Title : "Chat";
        ActiveSessionId = sessionId;
        Messages.Clear();
        HiddenMessages = 0;
        _messagesById.Clear();
        IsBusy = false;
        StatusMessage = "";
        ResetTurnFlags();
        ResetRevertState();
        ClearPendingPrompts();
        RebuildActiveSubagents();

        // Viewing the session now; clear any unread marker for it.
        _unread[sessionId] = false;
        if (known is not null) known.IsUnread = false;

        if (known is not null)
        {
            ApplySessionSettings(known);
        }
        else
        {
            // Session not in the sidebar (e.g. a subagent whose session.created event raced the
            // click, or a session from another workspace). Fetch its info so the header shows a
            // real title, the mode/model reflect its agent, and the back button knows the parent.
            try
            {
                var info = await _client.GetSessionAsync(sessionId);
                if (info is not null)
                {
                    if (info.Title.Length > 0) SessionTitle = info.Title;
                    ParentSessionId = info.ParentId;
                    ApplySessionSettings(info);
                }
            }
            catch
            {
                // Fall back to the placeholder title; the message fetch below still works.
            }
        }

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
            await RefreshMcpStatusAsync();
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Returns to the parent session of the currently-active subagent session. No-op for
    /// root sessions. Mirrors the TUI's "go to parent session" navigation.
    /// </summary>
    public async Task GoToParentAsync()
    {
        if (ParentSessionId.Length == 0) return;
        await SwitchSessionAsync(ParentSessionId);
    }

    /// <summary>
    /// Undoes the agent's reply to the last user message. Mirrors the TUI's <c>session.undo</c>
    /// command: aborts if the session is busy (the server 409s a revert while busy), targets the
    /// last user message before the current revert point (so a second undo walks further back),
    /// calls POST /session/{id}/revert, and restores the undone user prompt (text + staged
    /// images) into the composer. No message refetch is needed — the server keeps reverted
    /// messages until the next prompt, and the chat page hides messages at/after the revert
    /// point via <see cref="RevertMessageId"/>.
    /// </summary>
    public async Task UndoLastMessageAsync()
    {
        if (_sessionId.Length == 0) return;
        try
        {
            var target = FindUndoTargetMessage();
            if (target is null) return;

            if (IsBusy) await _client.AbortAsync(_sessionId);

            await _client.RevertAsync(_sessionId, target.Id);

            RestorePromptFromMessage(target);
            ApplyRevertMarker(target.Id);
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Restores reverted messages. If a user message exists beyond the revert point, reverts
    /// forward to it; otherwise clears the revert entirely (unrevert). Mirrors the TUI's
    /// <c>session.redo</c> command.
    /// </summary>
    public async Task RedoLastMessageAsync()
    {
        if (_sessionId.Length == 0 || RevertMessageId.Length == 0) return;
        try
        {
            var next = Messages
                .Where(m => m.Role == "user" && StringComparer.Ordinal.Compare(m.Id, RevertMessageId) > 0)
                .OrderBy(m => m.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (next is null)
            {
                await _client.UnrevertAsync(_sessionId);
                ResetRevertState();
                return;
            }

            await _client.RevertAsync(_sessionId, next.Id);
            ApplyRevertMarker(next.Id);
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// The next undo target: the last user message strictly before the current revert point
    /// (a second undo walks further back), or the last user message overall when nothing is
    /// reverted yet. Null when there is nothing left to undo.
    /// </summary>
    private MessageItem? FindUndoTargetMessage()
    {
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            var m = Messages[i];
            if (m.Role != "user") continue;
            if (RevertMessageId.Length > 0 && StringComparer.Ordinal.Compare(m.Id, RevertMessageId) >= 0) continue;
            return m;
        }
        return null;
    }

    /// <summary>Sets the revert point and recomputes the card label.</summary>
    private void ApplyRevertMarker(string messageId)
    {
        RevertMessageId = messageId;
        RevertCountLabel = ComputeRevertCountLabel(messageId);
    }

    /// <summary>
    /// "N message(s) reverted" — counts the reverted user messages (id &gt;= the revert
    /// point, both user and assistant messages are hidden from view but only user messages
    /// are counted, matching the TUI's reverted-count).
    /// </summary>
    private string ComputeRevertCountLabel(string messageId)
    {
        if (messageId.Length == 0) return "";
        var count = Messages.Count(m => m.Role == "user" && StringComparer.Ordinal.Compare(m.Id, messageId) >= 0);
        return count == 1 ? "1 message reverted" : $"{count} messages reverted";
    }

    /// <summary>Clears the undo state. Called on connect / new / switch / delete / unrevert.</summary>
    private void ResetRevertState()
    {
        RevertMessageId = "";
        RevertCountLabel = "";
        RevertPromptText = "";
    }

    /// <summary>
    /// Restores the undone user message's prompt into the composer: concatenated non-synthetic
    /// text parts (TUI skips synthetic) plus its data-URL image file parts re-staged as pending
    /// attachments. Matches the TUI/web undo behavior.
    /// </summary>
    private void RestorePromptFromMessage(MessageItem message)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var part in message.Parts)
        {
            if (part.Type == "text" && !part.Synthetic) sb.Append(part.Text);
        }
        RevertPromptText = sb.ToString();

        PendingImages.Clear();
        PendingImageCount = 0;
        foreach (var part in message.Parts)
        {
            if (part.Type != "file") continue;
            var attachment = AttachmentFromPart(part);
            if (attachment is null) continue;
            PendingImages.Add(attachment);
            PendingImageCount = PendingImages.Count;
        }
    }

    /// <summary>Rebuilds an <see cref="ImageAttachment"/> from a data-URL image file part; null when not decodable.</summary>
    private static ImageAttachment? AttachmentFromPart(PartItem part)
    {
        if (!part.IsImage || !part.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
        var comma = part.Url.IndexOf(',');
        if (comma < 0) return null;
        try
        {
            var bytes = Convert.FromBase64String(part.Url.Substring(comma + 1));
            var attachment = new ImageAttachment
            {
                FileName = part.FileName.Length > 0 ? part.FileName : "attachment",
                Mime = part.Mime.Length > 0 ? part.Mime : "image/png",
                Bytes = bytes,
            };
            // Decode fire-and-forget like PartItem.LoadImageAsync; the await resumes on the
            // UI thread so the thumbnail strip updates once the bitmap is ready.
            _ = DecodePreviewAsync(attachment);
            return attachment;
        }
        catch
        {
            return null;
        }
    }

    private static async Task DecodePreviewAsync(ImageAttachment attachment)
    {
        attachment.Preview = await ImageAttachment.DecodeAsync(attachment.Bytes);
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
        LoadPartImages(item);
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
        // from another client). session.status, message.updated, and the question events are NOT
        // filtered either: they carry the busy/unread/outcome/attention data that drives the
        // sidebar indicators for every session, not just the active one (message.updated for a
        // background session only feeds the outcome tracker and never touches the active message
        // list, and question events only feed the pending-attention tracker).
        if (evt.Type is "message.part.updated" or "message.part.delta"
            or "message.part.removed" or "message.removed" or "session.idle"
            or "session.error" or "session.diff" or "session.compacted")
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
                ApplyMessageRemoved(evt.Properties);
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
            case "question.rejected":
                ApplyQuestionReplied(evt.Properties);
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
                // An MCP server's tool set changed (or its connection closed). The server
                // doesn't push a status event for connect/disconnect, so re-poll GET /mcp.
                _ = RefreshMcpStatusAsync();
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

        var sessionId = properties.GetStringProperty("sessionID");
        if (sessionId.Length > 0 && sessionId != _sessionId)
        {
            // Background session: record the turn outcome for the sidebar indicator without
            // touching the active session's message list. message.updated fires with the final
            // info (error/finish/cost/tokens) once the assistant message completes, so the last
            // update we see for a turn carries its definitive outcome.
            if (info.GetStringProperty("role") == "assistant")
                _sessionOutcome[sessionId] = ClassifyMessageOutcome(info);
            return;
        }

        if (_messagesById.TryGetValue(id, out var message))
        {
            var role = info.GetStringProperty("role");
            if (role.Length > 0) message.Role = role;
            ApplyMessageStats(message, info);
            MarkInterrupted(message, info);
            ApplyMessageError(message, info);
            if (info.TryGetProperty("finish", out _)) OnTurnCompleted();
            // The server emits status idle before the final message.updated carrying the
            // error, so surface the Continue button here when the turn is already stopped.
            if (!IsBusy && LastAssistantMessageErrored()) TurnStoppedWithError = true;
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
        if (!IsBusy && LastAssistantMessageErrored()) TurnStoppedWithError = true;
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
    /// Classifies how an assistant message's turn ended: "success" (no error), "interrupted"
    /// (<c>MessageAbortedError</c> — user stopped it), or "error" (any other error). Mirrors
    /// the opencode web client's turn-outcome logic (rows.ts interrupted/error detection).
    /// </summary>
    private static string ClassifyMessageOutcome(JsonElement info)
    {
        if (!info.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
            return "success";
        return error.GetStringProperty("name") == "MessageAbortedError" ? "interrupted" : "error";
    }

    /// <summary>
    /// True when the active session's most recent assistant message carries a non-interrupt
    /// error part (i.e. the last turn stopped with an error). Interrupts are MessageAbortedError
    /// → aborted part, not an error part, so they never qualify.
    /// </summary>
    private bool LastAssistantMessageErrored()
    {
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            var m = Messages[i];
            if (m.Role != "assistant") continue;
            return m.Parts.Any(p => p.Type == "error");
        }
        return false;
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

    /// <summary>
    /// Clears the end-of-chat retry card and "Continue" state. Called when the active session
    /// is reset (connect / new / switch / deleted) and before sending a new prompt.
    /// </summary>
    private void ResetTurnFlags()
    {
        TurnStoppedWithError = false;
        IsRetrying = false;
        RetryMessage = "";
        RetryAttempt = 0;
        RetryNextMs = 0;
        RetryCountdown = "";
    }

    /// <summary>
    /// Recomputes the live countdown for the end-of-chat retry card. The chat page ticks this
    /// once per second while a turn is auto-retrying (<see cref="RetryNextMs"/> is the absolute
    /// unix-ms time the server will fire the next attempt at).
    /// </summary>
    public void UpdateRetryCountdown()
    {
        if (!IsRetrying)
        {
            RetryCountdown = "";
            return;
        }
        if (RetryNextMs <= 0)
        {
            RetryCountdown = RetryAttempt > 0 ? $"Attempt #{RetryAttempt} · retrying…" : "Retrying…";
            return;
        }
        var seconds = Math.Max(0, (int)Math.Ceiling((RetryNextMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000.0));
        RetryCountdown = RetryAttempt > 0 ? $"Attempt #{RetryAttempt} · retrying in {seconds}s" : $"Retrying in {seconds}s";
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
            _ = p.LoadImageAsync();
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

    /// <summary>
    /// Drops a message from the UI. The server emits this when a reverted session's messages
    /// are cleaned up at the start of the next prompt (SessionRevert.cleanup), and for other
    /// message removals. The message is scoped to the active session (see the filter in
    /// <see cref="Apply"/>), so only the active list is touched.
    /// </summary>
    private void ApplyMessageRemoved(JsonElement properties)
    {
        var id = properties.GetStringProperty("messageID");
        if (id.Length == 0) return;
        if (!_messagesById.TryGetValue(id, out var message)) return;

        _messagesById.Remove(id);
        Messages.Remove(message);
        UpdateSessionStats();
    }

    private void ApplySessionStatus(JsonElement properties)
    {
        if (!properties.TryGetProperty("status", out var status)) return;
        var type = status.GetStringProperty("type");

        // Track busy/unread for every session the stream reports on, so the sidebar
        // indicators stay live even for background sessions.
        var sessionId = properties.GetStringProperty("sessionID");
        if (sessionId.Length > 0)
        {
            _sessionStatus[sessionId] = type;
            var item = Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (item is not null)
            {
                item.IsBusy = type != "idle";
                // A turn finished in a session we aren't looking at → flag it unread, with the
                // outcome already tracked from the turn's final message.updated.
                if (type == "idle" && sessionId != _sessionId)
                {
                    _unread[sessionId] = true;
                    item.IsUnread = true;
                    item.Outcome = _sessionOutcome.GetValueOrDefault(sessionId) ?? "";
                }
            }
        }

        // The active-session banner (IsBusy/StatusMessage) only applies to the current session.
        if (sessionId.Length > 0 && sessionId != _sessionId) return;

        IsBusy = type != "idle";

        if (type == "retry")
        {
            var message = status.GetStringProperty("message");
            var attempt = status.GetInt64Property("attempt");
            var next = status.GetInt64Property("next");
            var prefix = attempt > 0 ? $"Retry #{attempt}" : "Retry";
            StatusMessage = message.Length > 0 ? $"{prefix}: {message}" : prefix;

            IsRetrying = true;
            RetryMessage = message;
            RetryAttempt = (int)attempt;
            RetryNextMs = next;
            UpdateRetryCountdown();
        }
        else
        {
            StatusMessage = "";
            IsRetrying = false;
            RetryMessage = "";
            RetryAttempt = 0;
            RetryNextMs = 0;
            RetryCountdown = "";

            // The turn finished. If it stopped because of a non-interrupt error, surface the
            // "Continue" button. (Interrupts are MessageAbortedError → aborted part instead.)
            if (type == "idle") TurnStoppedWithError = LastAssistantMessageErrored();
        }

        if (!IsBusy) _ = DrainPendingPromptsAsync();
    }

    private void ApplyQuestionAsked(JsonElement properties)
    {
        var requestId = properties.GetStringProperty("id");
        if (requestId.Length == 0) return;

        // Track the pending question per session for the sidebar attention indicator. The active
        // session's question is also attached inline below; background sessions just get counted.
        var sessionId = properties.GetStringProperty("sessionID");
        if (sessionId.Length > 0)
        {
            _pendingQuestions[sessionId] = _pendingQuestions.GetValueOrDefault(sessionId) + 1;
            var item = Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (item is not null) ApplySessionFlags(item);
        }
        if (sessionId.Length > 0 && sessionId != _sessionId) return;

        if (!properties.TryGetProperty("tool", out var tool)) return;

        var messageId = tool.GetStringProperty("messageID");
        var callId = tool.GetStringProperty("callID");
        if (messageId.Length == 0 || callId.Length == 0) return;

        if (!_messagesById.TryGetValue(messageId, out var message)) return;
        var part = message.Parts.FirstOrDefault(p => p.CallId == callId);
        if (part is null) return;

        AttachQuestion(part, requestId, properties);
    }

    /// <summary>Clears a session's pending-question count when a question is answered or dismissed.</summary>
    private void ApplyQuestionReplied(JsonElement properties)
    {
        var sessionId = properties.GetStringProperty("sessionID");
        if (sessionId.Length == 0) return;
        if (_pendingQuestions.TryGetValue(sessionId, out var count) && count > 0)
            _pendingQuestions[sessionId] = count - 1;
        var item = Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (item is not null) ApplySessionFlags(item);
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

        // Track the pending approval per session for the sidebar attention indicator, alongside
        // the active-view queue below.
        var sessionId = properties.GetStringProperty("sessionID");
        if (sessionId.Length > 0)
        {
            _pendingPermissions[sessionId] = _pendingPermissions.GetValueOrDefault(sessionId) + 1;
            var item = Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (item is not null) ApplySessionFlags(item);
        }

        AddPermissionRequest(PermissionRequestItem.FromJson(properties));
    }

    private void ApplyPermissionReplied(JsonElement properties)
    {
        var requestId = properties.GetStringProperty("requestID");
        if (requestId.Length > 0) RemovePermissionRequest(requestId);

        var sessionId = properties.GetStringProperty("sessionID");
        if (sessionId.Length > 0)
        {
            if (_pendingPermissions.TryGetValue(sessionId, out var count) && count > 0)
                _pendingPermissions[sessionId] = count - 1;
            var item = Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (item is not null) ApplySessionFlags(item);
        }
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
    /// Re-syncs pending questions from the server: rebuilds the per-session pending-question
    /// counts (drives the sidebar attention indicator) and re-attaches requestIDs to the active
    /// session's tool parts after a reload (requestIDs only exist in the live question.asked
    /// event and the server's in-memory pending map, not in the persisted message parts).
    /// </summary>
    public async Task SyncPendingQuestionsAsync()
    {
        try
        {
            var root = await _client.GetPendingQuestionsAsync();
            if (root.ValueKind != JsonValueKind.Array) return;

            _pendingQuestions.Clear();
            foreach (var question in root.EnumerateArray())
            {
                var sessionId = question.GetStringProperty("sessionID");
                if (sessionId.Length == 0) continue;
                _pendingQuestions[sessionId] = _pendingQuestions.GetValueOrDefault(sessionId) + 1;

                if (sessionId != _sessionId) continue;
                if (!question.TryGetProperty("tool", out var tool)) continue;

                var messageId = tool.GetStringProperty("messageID");
                var callId = tool.GetStringProperty("callID");
                if (messageId.Length == 0 || callId.Length == 0) continue;
                if (!_messagesById.TryGetValue(messageId, out var message)) continue;

                var part = message.Parts.FirstOrDefault(p => p.CallId == callId && p.ToolName == "question");
                if (part is null || part.QuestionRequestId.Length > 0) continue;

                AttachQuestion(part, question.GetStringProperty("id"), question);
            }
            RefreshSessionFlags();
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Re-syncs pending permission requests from the server: rebuilds the per-session pending
    /// counts (drives the sidebar attention indicator) and re-queues requests for the active
    /// view after a reload. When no session is active yet (e.g. right after connect), only the
    /// counts are seeded — the active-view approval dialog is not shown until a session is open.
    /// </summary>
    public async Task SyncPendingPermissionsAsync()
    {
        try
        {
            var root = await _client.GetPendingPermissionsAsync();
            if (root.ValueKind != JsonValueKind.Array) return;

            _pendingPermissions.Clear();
            foreach (var request in root.EnumerateArray())
            {
                var id = request.GetStringProperty("id");
                if (id.Length == 0) continue;
                var sessionId = request.GetStringProperty("sessionID");
                if (sessionId.Length > 0)
                    _pendingPermissions[sessionId] = _pendingPermissions.GetValueOrDefault(sessionId) + 1;
                if (_sessionId.Length == 0) continue;
                AddPermissionRequest(PermissionRequestItem.FromJson(request));
            }
            RefreshSessionFlags();
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
        {
            item.Mime = part.GetStringProperty("mime");
            item.Url = part.GetStringProperty("url");
            item.FileName = part.GetStringProperty("filename") != "" ? part.GetStringProperty("filename") : item.Url;
        }

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
                if (input.TryGetProperty("subagent_type", out var subType)) item.ToolSubagentType = subType.GetString() ?? "";
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
                // The task tool records the spawned subagent session here (see task.ts metadata).
                if (meta.TryGetProperty("sessionId", out var mSession)) item.ToolSessionId = mSession.GetString() ?? "";
                if (meta.TryGetProperty("parentSessionId", out var mParent)) item.ToolParentSessionId = mParent.GetString() ?? "";
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
        UsageTokensInput = 0;
        UsageTokensOutput = 0;
        UsageTokensReasoning = 0;
        UsageTokensCacheRead = 0;
        UsageTokensCacheWrite = 0;
        ContextLimit = 0;
    }

    private void UpdateSessionStats()
    {
        var last = Messages.LastOrDefault(m => m.Role == "assistant" && m.TokensOutput > 0);
        if (last is null)
        {
            ResetUsageStats();
            return;
        }

        var sessionCost = Messages.Where(m => m.Role == "assistant").Sum(m => m.Cost);
        UsageCostLabel = FormatCost(sessionCost);

        UsageTokensInput = last.TokensInput;
        UsageTokensOutput = last.TokensOutput;
        UsageTokensReasoning = last.TokensReasoning;
        UsageTokensCacheRead = last.TokensCacheRead;
        UsageTokensCacheWrite = last.TokensCacheWrite;

        var tokens = last.TokensInput + last.TokensOutput + last.TokensReasoning
            + last.TokensCacheRead + last.TokensCacheWrite;
        UsageTokensLabel = tokens.ToString("N0");

        var limit = ResolveContextLimit(last);
        ContextLimit = limit;
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
        {
            item.Mime = part.GetStringProperty("mime");
            item.Url = part.GetStringProperty("url");
            item.FileName = part.GetStringProperty("filename") != "" ? part.GetStringProperty("filename") : item.Url;
        }
    }

    /// <summary>Kicks off async decode of image file parts so message thumbnails render.</summary>
    private static void LoadPartImages(MessageItem item)
    {
        foreach (var part in item.Parts)
        {
            if (part.Type == "file") _ = part.LoadImageAsync();
        }
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
