using System.Text.Json;
using System.Threading.Channels;
using UnoVibe.Models;
using static QuickMarkup.Infra.QuickRefs;

namespace UnoVibe.Services;

/// <summary>
/// Reactive store for the current chat session. Holds messages and applies SSE events
/// to them. All mutations happen on the UI thread via the dispatcher pump.
/// </summary>
public sealed class ChatStore
{
    public static ChatStore Instance { get; } = new();

    private static readonly JsonSerializerOptions JsonDefaults = new() { WriteIndented = false };

    // Refs are created here, so the singleton must first be accessed on the UI thread.
    public Reference<bool> IsBusyProp { get; } = Ref(false);
    public bool IsBusy { get => IsBusyProp.Value; set => IsBusyProp.Value = value; }

    public Reference<string> ConnectionStatusProp { get; } = Ref("Connecting...");
    public string ConnectionStatus { get => ConnectionStatusProp.Value; set => ConnectionStatusProp.Value = value; }

    public Reference<string> SessionTitleProp { get; } = Ref("New Chat");
    public string SessionTitle { get => SessionTitleProp.Value; set => SessionTitleProp.Value = value; }

    public Reference<string> SelectedSessionIdProp { get; } = Ref("");
    public string SelectedSessionId { get => SelectedSessionIdProp.Value; set => SelectedSessionIdProp.Value = value; }

    public ObservableCollection<MessageItem> Messages { get; } = new();
    public ObservableCollection<SessionInfo> Sessions { get; } = new();

    public string CurrentSessionId => _sessionId;

    private readonly OpencodeClient _client;
    private readonly Channel<OpencodeEvent> _events = Channel.CreateUnbounded<OpencodeEvent>();
    private readonly Dictionary<string, MessageItem> _messagesById = new();
    private CancellationTokenSource? _cts;
    private DispatcherQueue? _dispatcher;
    private bool _started;
    private string _sessionId = "";

    private ChatStore()
    {
        var baseUrl = Environment.GetEnvironmentVariable("OPENCODE_BASE_URL") ?? "http://localhost:4096";
        _client = new OpencodeClient(baseUrl);
    }

    public async Task ConnectAsync()
    {
        if (_started) return;
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
            ConnectionStatus = healthy ? "Connected" : "Error: health check failed";
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
            return;
        }

        try
        {
            _sessionId = await _client.CreateSessionAsync("New Chat", ct) ?? "";
            if (_sessionId.Length == 0) ConnectionStatus = "Error: could not create session";
            else await RefreshSessionsAsync(ct);
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    public async Task SendAsync(string text)
    {
        if (_sessionId.Length == 0) return;
        try
        {
            await _client.SendPromptAsync(_sessionId, text);
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    public async Task RefreshSessionsAsync(CancellationToken ct = default)
    {
        try
        {
            var list = await _client.ListSessionsAsync(ct);
            Sessions.Clear();
            foreach (var session in list) Sessions.Add(session);
            if (Sessions.Any(s => s.Id == _sessionId)) SelectedSessionId = _sessionId;
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    public async Task NewSessionAsync()
    {
        try
        {
            var id = await _client.CreateSessionAsync("New Chat") ?? "";
            if (id.Length == 0) return;
            _sessionId = id;
            SessionTitle = "New Chat";
            Messages.Clear();
            _messagesById.Clear();
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
        Messages.Clear();
        _messagesById.Clear();
        IsBusy = false;
        SelectedSessionId = sessionId;

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
            or "message.part.removed" or "session.status")
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
            if (info.TryGetProperty("finish", out _)) IsBusy = false;
            return;
        }

        message = new MessageItem
        {
            Id = id,
            Role = info.GetStringProperty("role"),
            Agent = info.GetStringProperty("agent"),
        };
        if (info.TryGetProperty("finish", out _)) IsBusy = false;
        _messagesById[id] = message;
        Messages.Add(message);
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

    private static PartItem PartFromJson(JsonElement part)
    {
        var item = new PartItem
        {
            Id = part.GetStringProperty("id"),
            MessageId = part.GetStringProperty("messageID"),
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
                if (input.TryGetProperty("workdir", out var workdir)) item.ToolWorkdir = workdir.GetString() ?? "";
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
                if (meta.TryGetProperty("loaded", out var mLoaded) && mLoaded.ValueKind == JsonValueKind.Array)
                    item.LoadedFiles = string.Join("\n", mLoaded.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0));
            }
        }
        if (string.IsNullOrEmpty(item.ToolTitle)) item.ToolTitle = item.ToolName;
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
}
