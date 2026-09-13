using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using UnoVibe.Integration;
using UnoVibe.Integration.Events;

namespace UnoVibe.Providers;

class EventsProvider
{
    OpencodeClient client;
    DispatcherQueue dispatcherQueue;
    public EventsProvider(OpencodeClient client, DispatcherQueue dispatcherQueue)
    {
        this.client = client;
        this.dispatcherQueue = dispatcherQueue;
    }
    readonly Dictionary<string, Dictionary<string, Action<string, JsonElement>>> registered = [];
    readonly Dictionary<string, Action<string, JsonElement>> registeredForAllDirectories = [];
    private void SubscribeToDirectory(string directory)
    {
        if (registered.ContainsKey(directory)) return;
        registered[directory] = [];
        _ = Task.Run(() => client.ReadEventAsync(channel.Writer, cts.Token));
        _ = Task.Run(() => PumpAsync());
    }

    public void Register(string? directory, string eventType, Action<string, JsonElement> handler)
    {
        Dictionary<string, Action<string, JsonElement>> dict;
        if (directory is null)
        {
            dict = registeredForAllDirectories;
        } else
        {
            SubscribeToDirectory(directory);
            dict = registered[directory];
        }
        if (dict.ContainsKey(eventType))
        {
            dict[eventType] += handler;
        } else
        {
            dict[eventType] = handler;
        }
    }

    public void Unregister(string? directory, string eventType, Action<string, JsonElement> handler)
    {
        Dictionary<string, Action<string, JsonElement>> dict;
        if (directory is null)
        {
            dict = registeredForAllDirectories;
        } else if (!registered.TryGetValue(directory, out dict!))
        {
            // nothing to unregister
            return;
        }
        if (dict.ContainsKey(eventType))
        {
            var result = dict[eventType] - handler;
            if (result is null)
                dict.Remove(eventType);
            else
                dict[eventType] = result;
        } else
        {
            // nothing to unregister
        }
    }
    private void UnregisterDelegate(string? directory, string eventType, Delegate handler)
        => Unregister(directory, eventType, delegateMapping[handler]);
    public void Register(string directory) => SubscribeToDirectory(directory);
    public void RegisterMessageUpdated(string? directory, Action<string, MessageUpdatedEvent> handler)
        => Register(directory, EventTypes.MessageUpdated, MakeHandler(handler, AppJsonContext.Default.MessageUpdatedEvent));
    public void RegisterMessagePartUpdated(string? directory, Action<string, MessagePartUpdatedEvent> handler)
        => Register(directory, EventTypes.MessagePartUpdated, MakeHandler(handler, AppJsonContext.Default.MessagePartUpdatedEvent));
    public void RegisterMessagePartDelta(string? directory, Action<string, MessagePartDeltaEvent> handler)
        => Register(directory, EventTypes.MessagePartDelta, MakeHandler(handler, AppJsonContext.Default.MessagePartDeltaEvent));
    public void RegisterMessagePartRemoved(string? directory, Action<string, MessagePartRemovedEvent> handler)
        => Register(directory, EventTypes.MessagePartRemoved, MakeHandler(handler, AppJsonContext.Default.MessagePartRemovedEvent));
    public void RegisterMessageRemoved(string? directory, Action<string, MessageRemovedEvent> handler)
        => Register(directory, EventTypes.MessageRemoved, MakeHandler(handler, AppJsonContext.Default.MessageRemovedEvent));
    public void RegisterSessionCreated(string? directory, Action<string, SessionCrudEvent> handler)
        => Register(directory, EventTypes.SessionCreated, MakeHandler(handler, AppJsonContext.Default.SessionCrudEvent));
    public void RegisterSessionStatus(string? directory, Action<string, SessionStatusEvent> handler)
        => Register(directory, EventTypes.SessionStatus, MakeHandler(handler, AppJsonContext.Default.SessionStatusEvent));
    public void RegisterSessionUpdated(string? directory, Action<string, SessionCrudEvent> handler)
        => Register(directory, EventTypes.SessionUpdated, MakeHandler(handler, AppJsonContext.Default.SessionCrudEvent));
    public void RegisterSessionDeleted(string? directory, Action<string, SessionCrudEvent> handler)
        => Register(directory, EventTypes.SessionDeleted, MakeHandler(handler, AppJsonContext.Default.SessionCrudEvent));
    public void RegisterSessionError(string? directory, Action<string, SessionErrorEvent> handler)
        => Register(directory, EventTypes.SessionError, MakeHandler(handler, AppJsonContext.Default.SessionErrorEvent));
    public void RegisterSessionDiff(string? directory, Action<string, SessionDiffEvent> handler)
        => Register(directory, EventTypes.SessionDiff, MakeHandler(handler, AppJsonContext.Default.SessionDiffEvent));
    public void RegisterSessionIdle(string? directory, Action<string, SessionIdleEvent> handler)
        => Register(directory, EventTypes.SessionIdle, MakeHandler(handler, AppJsonContext.Default.SessionIdleEvent));
    public void RegisterSessionCompacted(string? directory, Action<string, SessionCompactedEvent> handler)
        => Register(directory, EventTypes.SessionCompacted, MakeHandler(handler, AppJsonContext.Default.SessionCompactedEvent));
    public void RegisterQuestionAsked(string? directory, Action<string, QuestionAskedEvent> handler)
        => Register(directory, EventTypes.QuestionAsked, MakeHandler(handler, AppJsonContext.Default.QuestionAskedEvent));
    public void RegisterQuestionReplied(string? directory, Action<string, QuestionRepliedEvent> handler)
        => Register(directory, EventTypes.QuestionReplied, MakeHandler(handler, AppJsonContext.Default.QuestionRepliedEvent));
    public void RegisterQuestionRejected(string? directory, Action<string, QuestionRejectedEvent> handler)
        => Register(directory, EventTypes.QuestionRejected, MakeHandler(handler, AppJsonContext.Default.QuestionRejectedEvent));
    public void RegisterPermissionAsked(string? directory, Action<string, PermissionAskedEvent> handler)
        => Register(directory, EventTypes.PermissionAsked, MakeHandler(handler, AppJsonContext.Default.PermissionAskedEvent));
    public void RegisterPermissionReplied(string? directory, Action<string, PermissionRepliedEvent> handler)
        => Register(directory, EventTypes.PermissionReplied, MakeHandler(handler, AppJsonContext.Default.PermissionRepliedEvent));
    public void RegisterFileEdited(string? directory, Action<string, FileEditedEvent> handler)
        => Register(directory, EventTypes.FileEdited, MakeHandler(handler, AppJsonContext.Default.FileEditedEvent));
    public void RegisterFileWatcherUpdated(string? directory, Action<string, FileWatcherUpdatedEvent> handler)
        => Register(directory, EventTypes.FileWatcherUpdated, MakeHandler(handler, AppJsonContext.Default.FileWatcherUpdatedEvent));
    public void RegisterVcsBranchUpdated(string? directory, Action<string, VcsBranchUpdatedEvent> handler)
        => Register(directory, EventTypes.VcsBranchUpdated, MakeHandler(handler, AppJsonContext.Default.VcsBranchUpdatedEvent));
    public void RegisterTodoUpdated(string? directory, Action<string, TodoUpdatedEvent> handler)
        => Register(directory, EventTypes.TodoUpdated, MakeHandler(handler, AppJsonContext.Default.TodoUpdatedEvent));
    public void RegisterLspUpdated(string? directory, Action<string, LspUpdatedEvent> handler)
        => Register(directory, EventTypes.LspUpdated, MakeHandler(handler, AppJsonContext.Default.LspUpdatedEvent));
    public void RegisterCommandExecuted(string? directory, Action<string, CommandExecutedEvent> handler)
        => Register(directory, EventTypes.CommandExecuted, MakeHandler(handler, AppJsonContext.Default.CommandExecutedEvent));
    public void RegisterMcpToolsChanged(string? directory, Action<string, McpToolsChangedEvent> handler)
        => Register(directory, EventTypes.McpToolsChanged, MakeHandler(handler, AppJsonContext.Default.McpToolsChangedEvent));
    public void RegisterMcpBrowserOpenFailed(string? directory, Action<string, McpBrowserOpenFailedEvent> handler)
        => Register(directory, EventTypes.McpBrowserOpenFailed, MakeHandler(handler, AppJsonContext.Default.McpBrowserOpenFailedEvent));
    public void RegisterServerConnected(string? directory, Action<string, ServerConnectedEvent> handler)
        => Register(directory, EventTypes.ServerConnected, MakeHandler(handler, AppJsonContext.Default.ServerConnectedEvent));
    // Heartbeat has no typed model — synthetic event with empty properties.
    public void RegisterServerHeartbeat(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "server.heartbeat", handler);
    public void RegisterServerInstanceDisposed(string? directory, Action<string, ServerInstanceDisposedEvent> handler)
        => Register(directory, EventTypes.ServerInstanceDisposed, MakeHandler(handler, AppJsonContext.Default.ServerInstanceDisposedEvent));
    public void RegisterTuiToastShow(string? directory, Action<string, TuiToastShowEvent> handler)
        => Register(directory, EventTypes.TuiToastShow, MakeHandler(handler, AppJsonContext.Default.TuiToastShowEvent));
    
    // An MCP server's tool set changed (or its connection closed). The server
    // doesn't push a status event for connect/disconnect, so re-poll GET /mcp.
    public void UnregisterMcpToolsChanged(string? directory, Action<string, McpToolsChangedEvent> handler)
        => UnregisterDelegate(directory, EventTypes.McpToolsChanged, handler);

    Action<string, JsonElement> MakeHandler<T>(Action<string, T> handler, JsonTypeInfo<T> typeInfo)
    {
        if (!delegateMapping.TryGetValue(handler, out var mapped))
        {
            delegateMapping[handler] = mapped = (dir, json) =>
            {
                handler(dir, json.Deserialize(typeInfo)!);
            };
        }
        return mapped;
    }
    readonly Dictionary<Delegate, Action<string, JsonElement>> delegateMapping = [];

    void Apply(OpencodeEvent evt)
    {
        if (evt.Directory is null || !registered.TryGetValue(evt.Directory, out var registeredDir))
            return;
        if (!registeredDir.TryGetValue(evt.Type, out var handlers))
            return;
        handlers?.Invoke(evt.Directory, evt.Properties);
    }
    CancellationTokenSource cts = new();
    Channel<OpencodeEvent> channel = Channel.CreateUnbounded<OpencodeEvent>();
    readonly HashSet<string> _seenEventIds = new();
    readonly Queue<string> _seenEventIdOrder = new();

    private async Task PumpAsync()
    {
        var ct = cts.Token;
        var reader = channel.Reader;
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
            while (reader.TryRead(out var evt))
            {
                if (IsDuplicateEvent(evt)) continue;
                batch.Add(evt);
            }

            dispatcherQueue?.TryEnqueue(() =>
            {
                foreach (var evt in batch) Apply(evt);
            });
        }
    }

    // Bounded set of recently-seen SSE event ids, used to drop duplicates when an opened folder
    // equals the server's default instance (both the default and the folder stream deliver the
    // same events, and double-applying part deltas would corrupt message text).
    private const int MaxSeenEventIds = 2000;
    /// <summary>
    /// Returns true when an SSE event id was already processed. Each stream instance generates
    /// globally-unique ids, but an opened folder that equals the server's default instance is
    /// delivered by both the default stream and its folder stream — the second copy is dropped.
    /// Ids are globally unique so the bounded set never false-positives across reconnects.
    /// </summary>
    private bool IsDuplicateEvent(OpencodeEvent evt)
    {
        if (string.IsNullOrEmpty(evt.Id)) return false;
        if (_seenEventIds.Contains(evt.Id)) return true;
        
        _seenEventIds.Add(evt.Id);
        _seenEventIdOrder.Enqueue(evt.Id);
        while (_seenEventIdOrder.Count > MaxSeenEventIds)
            _seenEventIds.Remove(_seenEventIdOrder.Dequeue());
        return false;
    }

}