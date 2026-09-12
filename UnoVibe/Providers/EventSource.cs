using System.Text.Json;
using System.Threading.Channels;
using UnoVibe.Integration;

namespace UnoVibe.Providers;

class EventSource
{
    OpencodeClient client;
    DispatcherQueue dispatcherQueue;
    public EventSource(OpencodeClient client, DispatcherQueue dispatcherQueue)
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
    public void Register(string directory) => SubscribeToDirectory(directory);
    public void RegisterMessageUpdated(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "message.updated", handler);
    public void RegisterMessagePartUpdated(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "message.part.updated", handler);
    public void RegisterMessagePartDelta(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "message.part.delta", handler);
    public void RegisterMessagePartRemoved(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "message.part.removed", handler);
    public void RegisterMessageRemoved(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "message.removed", handler);
    public void RegisterSessionCreated(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "session.created", handler);
    public void RegisterSessionStatus(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "session.status", handler);
    public void RegisterSessionUpdated(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "session.updated", handler);
    public void RegisterSessionDeleted(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "session.deleted", handler);
    // TODO: properties { sessionID?, error }; surface server-side session errors.
    public void RegisterSessionError(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "session.error", handler);
    // TODO: properties { sessionID, diff }; show file diffs produced by the session.
    public void RegisterSessionDiff(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "session.diff", handler);
    // TODO: properties { sessionID }; deprecated — superseded by session.status {type:"idle"}.
    public void RegisterSessionIdle(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "session.idle", handler);
    // TODO: properties { sessionID }; mark the session as compacted.
    public void RegisterSessionCompacted(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "session.compacted", handler);
    public void RegisterQuestionAsked(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "question.asked", handler);
    public void RegisterQuestioReplied(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "question.replied", handler);
    public void RegisterQuestionRejected(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "question.rejected", handler);
    public void RegisterPermissionAsked(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "permission.asked", handler);
    public void RegisterPermissionReplied(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "permission.replied", handler);
    // TODO: properties { file }; the agent edited a file on disk.
    public void RegisterFileEdited(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "file.edited", handler);
    // TODO: properties { file, event: "add"|"change"|"unlink" }.
    public void RegisterFileWatcherUpdated(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "file.watcher.updated", handler);
    // The git branch changed in a workspace. The payload only carries { branch }
    // (no directory), so refresh every sidebar directory group's branch label.
    public void RegisterVcsBranchUpdated(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "vcs.branch.updated", handler);
    // TODO: the todo list changed; the TUI renders it inline.
    public void RegisterTodoUpdated(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "todo.updated", handler);
    // TODO: LSP status changed; properties {}.
    public void RegisterLspUpdated(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "lsp.updated", handler);
    // TODO: a custom command was executed server-side.
    public void RegisterCommandExecuted(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "command.executed", handler);
    // An MCP server's tool set changed (or its connection closed). The server
    // doesn't push a status event for connect/disconnect, so re-poll GET /mcp.
    public void RegisterMcpToolsChanged(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "mcp.tools.changed", handler);
    // TODO: an MCP browser-open attempt failed.
    public void RegisterMcpBrowserOpenFailed(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "mcp.browser.open.failed", handler);
    // TODO: first event on the /event stream ({}); could drive connection state.
    public void RegisterServerConnected(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "server.connected", handler);
    // TODO: sent every 10s ({}) to keep the stream alive; ignoring is fine.
    public void RegisterServerHeartbeat(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "server.heartbeat", handler);
    // TODO: the server instance was disposed ({}); the stream ends after this event.
    public void RegisterServerInstanceDisposed(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "server.instance.disposed", handler);
    // TUI command plumbing (server → client commands; relevant only if adopting them)
    public void RegisterTuiToastShow(string? directory, Action<string, JsonElement> handler)
        => Register(directory, "tui.toast.show", handler);
    
    // An MCP server's tool set changed (or its connection closed). The server
    // doesn't push a status event for connect/disconnect, so re-poll GET /mcp.
    public void UnregisterMcpToolsChanged(string? directory, Action<string, JsonElement> handler)
        => Unregister(directory, "mcp.tools.changed", handler);
    // TODO: an MCP browser-open attempt failed.

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