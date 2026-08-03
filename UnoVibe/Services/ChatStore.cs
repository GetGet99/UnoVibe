using System.Text.Json;
using System.Threading.Channels;
using UnoVibe.Models;
using static QuickMarkup.Infra.QuickRefs;

namespace UnoVibe.Services;

/// <summary>
/// Reactive store for the current chat session. Holds messages and applies SSE events
/// to them. All mutations happen on the UI thread via the dispatcher pump.
/// </summary>
public sealed class ChatStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonDefaults = new() { WriteIndented = false };

    // Refs are created here, so the singleton must first be accessed on the UI thread.
    public Reference<bool> IsBusyProp { get; } = Ref(false);
    public bool IsBusy { get => IsBusyProp.Value; set => IsBusyProp.Value = value; }

    public Reference<string> ConnectionStatusProp { get; } = Ref("Connecting...");
    public string ConnectionStatus { get => ConnectionStatusProp.Value; set => ConnectionStatusProp.Value = value; }

    public Reference<string> SessionTitleProp { get; } = Ref("New Chat");
    public string SessionTitle { get => SessionTitleProp.Value; set => SessionTitleProp.Value = value; }

    public Reference<string> UsageCostLabelProp { get; } = Ref("$0.00");
    public string UsageCostLabel { get => UsageCostLabelProp.Value; set => UsageCostLabelProp.Value = value; }

    public Reference<string> UsageTokensLabelProp { get; } = Ref("0");
    public string UsageTokensLabel { get => UsageTokensLabelProp.Value; set => UsageTokensLabelProp.Value = value; }

    public Reference<string> ContextLabelProp { get; } = Ref("0%");
    public string ContextLabel { get => ContextLabelProp.Value; set => ContextLabelProp.Value = value; }

    public Reference<double> ContextUsageProp { get; } = Ref(0d);
    public double ContextUsage { get => ContextUsageProp.Value; set => ContextUsageProp.Value = value; }

    public Reference<string> ActiveSessionIdProp { get; } = Ref("");
    public string ActiveSessionId { get => ActiveSessionIdProp.Value; set => ActiveSessionIdProp.Value = value; }

    public Reference<string> ModeProp { get; } = Ref("build");
    public string Mode { get => ModeProp.Value; set => ModeProp.Value = value; }

    public Reference<string> ModelIdProp { get; } = Ref("");
    public string ModelId { get => ModelIdProp.Value; set => ModelIdProp.Value = value; }

    public Reference<string> ProviderIdProp { get; } = Ref("");
    public string ProviderId { get => ProviderIdProp.Value; set => ProviderIdProp.Value = value; }

    public Reference<string> VariantProp { get; } = Ref("Default");
    public string Variant { get => VariantProp.Value; set => VariantProp.Value = value; }

    public Reference<bool> HasVariantsProp { get; } = Ref(false);
    public bool HasVariants { get => HasVariantsProp.Value; set => HasVariantsProp.Value = value; }

    public ObservableCollection<string> ModeOptions { get; } = new();
    public ObservableCollection<ModelOption> ModelOptions { get; } = new();
    public ObservableCollection<string> VariantOptions { get; } = new();

    public ObservableCollection<MessageItem> Messages { get; } = new();
    public ObservableCollection<SessionInfo> Sessions { get; } = new();
    public ObservableCollection<DirectoryGroup> DirectoryGroups { get; } = new();

    public string CurrentSessionId => _sessionId;

    /// <summary>Owns any locally-launched <c>opencode serve</c> process so it stays alive after navigation.</summary>
    public ServeProcess? ServeProcess { get; private set; }

    private OpencodeClient _client = null!;
    private readonly Channel<OpencodeEvent> _events = Channel.CreateUnbounded<OpencodeEvent>();
    private readonly Dictionary<string, MessageItem> _messagesById = new();
    private CancellationTokenSource? _cts;
    private DispatcherQueue? _dispatcher;
    private bool _started;
    private string _sessionId = "";
    private string _baseUrl = "";
    private string? _password;
    private string? _username;

    public ChatStore()
    {
    }

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
        Messages.Clear();
        _messagesById.Clear();
        Sessions.Clear();
        DirectoryGroups.Clear();
        ConnectionStatus = "Connecting...";
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
            await _client.SendPromptAsync(_sessionId, text, Mode, ProviderId, ModelId, Variant);
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
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
            _sessionId = await _client.CreateSessionAsync("New Chat", null, Mode, ProviderId, ModelId, Variant) ?? "";
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
            foreach (var session in list) Sessions.Add(session);

            var groups = list
                .GroupBy(s => s.Directory)
                .Select(g => new DirectoryGroup
                {
                    Directory = g.Key.Length == 0 ? "(unknown)" : g.Key,
                    Sessions = new ObservableCollection<SessionInfo>(g.OrderByDescending(s => s.Updated)),
                })
                .OrderByDescending(g => g.Sessions.Count > 0 ? g.Sessions[0].Updated : 0)
                .ToList();

            DirectoryGroups.Clear();
            foreach (var group in groups) DirectoryGroups.Add(group);
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
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

    public async Task NewSessionAsync(string? directory = null)
    {
        try
        {
            var id = await _client.CreateSessionAsync("New Chat", directory, Mode, ProviderId, ModelId, Variant) ?? "";
            if (id.Length == 0) return;
            _sessionId = id;
            SessionTitle = "New Chat";
            ActiveSessionId = id;
            Messages.Clear();
            _messagesById.Clear();
            ResetUsageStats();
            IsBusy = false;
            await RefreshSessionsAsync();
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    public async Task SwitchSessionAsync(string sessionId)
    {
        if (sessionId.Length == 0 || sessionId == _sessionId) return;
        _sessionId = sessionId;
        SessionTitle = Sessions.FirstOrDefault(s => s.Id == sessionId)?.Title ?? "Chat";
        ActiveSessionId = sessionId;
        Messages.Clear();
        _messagesById.Clear();
        IsBusy = false;

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
                Messages.Add(message);
            }
            UpdateSessionStats();
            await SyncPendingQuestionsAsync();
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
                if (p.Id.Length > 0) item.Parts.Add(p);
            }
        }
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
        if (evt.Type is "message.updated" or "message.part.updated" or "message.part.delta"
            or "message.part.removed" or "session.status" or "question.asked" or "question.replied")
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
        }
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
            if (info.TryGetProperty("finish", out _)) IsBusy = false;
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
        if (info.TryGetProperty("finish", out _)) IsBusy = false;
        _messagesById[id] = message;
        Messages.Add(message);
        UpdateSessionStats();
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
            message.Parts.Add(PartFromJson(part));
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
        IsBusy = status.GetStringProperty("type") == "busy";
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
            if (title.Length > 0) item.ToolTitle = title;
            if (state.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object)
            {
                var serialized = JsonSerializer.Serialize(input, JsonDefaults);
                if (serialized != "{}") item.ToolInput = serialized;
                if (input.TryGetProperty("command", out var command)) item.ToolCommand = command.GetString() ?? "";
                if (input.TryGetProperty("filePath", out var filePath)) item.ToolFilePath = filePath.GetString() ?? "";
                if (input.TryGetProperty("pattern", out var pattern)) item.ToolPattern = pattern.GetString() ?? "";
                if (input.TryGetProperty("path", out var searchPath)) item.ToolSearchPath = searchPath.GetString() ?? "";
                if (input.TryGetProperty("include", out var include)) item.ToolInclude = include.GetString() ?? "";
                if (input.TryGetProperty("workdir", out var workdir)) item.ToolWorkdir = workdir.GetString() ?? "";
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

    private static void UpdatePart(PartItem item, JsonElement part)
    {
        if (item.Type is "text" or "reasoning" && part.TryGetProperty("text", out var text))
            item.Text = text.GetString() ?? "";

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
