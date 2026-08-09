using System.Text.Json;
using System.Threading.Channels;
using UnoVibe.Models;

namespace UnoVibe.Services;

/// <summary>
/// Router store for the whole chat window. Owns the connection (client, serve process, SSE
/// event pump), the sidebar state (sessions, directory groups, MCP servers), the shared
/// settings options (modes/models/variants), and the global permission/toast surfaces.
///
/// The per-session chat state lives in lazily-created, cached <see cref="SessionStore"/>s
/// keyed by session id. <see cref="Active"/> is the store for the currently-open session;
/// switching sessions re-points it, so an open session's messages and state survive
/// switching away and back (the cached store is reused, not recreated).
///
/// The mutable display fields are QuickMarkup reactive references (declared in the markup
/// header), so the pages bind to them directly. The references are created lazily on first
/// access, which must therefore happen on the UI thread for reactivity.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Models;
    public string ConnectionStatus = "Connecting...";
    public string DisplayLabel = "";
    // The server's default directory (from GET /path). Used as the reference point for
    // relative path display in the sidebar — so folders show relative to what the user
    // actually connected to, not the app's CWD.
    public string ServerDirectory = "";
    // The current connection's base URL and effective password (Basic auth). Populated in
    // Configure; shown in the sidebar's connection-details flyout. The password is masked in
    // the UI and only revealed on demand (the effective value, incl. env-var fallback).
    public string ConnectionUrl = "";
    public string ConnectionPassword = "";
    public string ActiveSessionId = "";
    // Number of subagent sessions belonging to the active session (ParentId == active session id).
    // Drives the chat page's subagent strip and the flyout's "Tokens (excludes subagents)" label.
    public int SubagentCount;
    // The permission request currently shown to the user (oldest pending), or null.
    public PermissionRequestItem? ActivePermission;
    public ToastItem? CurrentToast;
    // MCP servers for the active session's directory, in sidebar display order.
    public string McpDirectory = "";
    // Compact "N active, M inactive, K error" summary for the collapsed MCP sidebar header.
    // inactive = explicitly disabled; error = failed/needs_auth/needs_client_registration (mutually exclusive).
    public string McpSummary = "";
    // The store for the currently-open session (see the class doc). Never null after Ctor:
    // it starts as an unsaved draft and is re-pointed on switch/new/delete.
    public SessionStore? Active;
    """)]
public sealed partial class ChatStore : IDisposable
{
    public ObservableCollection<string> ModeOptions { get; } = new();
    public ObservableCollection<ModelOption> ModelOptions { get; } = new();
    public ObservableCollection<string> VariantOptions { get; } = new();

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

    /// <summary>The id of the currently-open session ("" for an unsaved draft).</summary>
    public string CurrentSessionId => Active?.SessionId ?? "";

    /// <summary>The HTTP client for the configured server (null before <see cref="Configure"/>).</summary>
    public OpencodeClient? Client => _client;

    /// <summary>Owns any locally-launched <c>opencode serve</c> process so it stays alive after navigation.</summary>
    public ServeProcess? ServeProcess { get; private set; }

    private OpencodeClient _client = null!;
    private readonly Channel<OpencodeEvent> _events = Channel.CreateUnbounded<OpencodeEvent>();
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
    private readonly List<PermissionRequestItem> _permissions = new();
    private CancellationTokenSource? _cts;
    private DispatcherQueue? _dispatcher;
    private bool _started;
    private string? _pendingDirectory;
    // Folders opened via the sidebar's "Open Folder" button (or a directory group's "+" button),
    // keyed by normalized path with the last-opened time (unix ms). These appear in the sidebar
    // even when the server reports no sessions for them yet (RebuildDirectoryGroups merges them in),
    // so a freshly-picked folder is visible before the first message creates a session.
    private readonly Dictionary<string, long> _openedFolders = new();
    // Per-opened-folder /event stream cancellation. The app's main /event stream is scoped to the
    // server's default instance, which filters out other directories' events — so sessions in a
    // picked folder get a second stream via /event?directory=<path>. Keyed by normalized path.
    private readonly Dictionary<string, CancellationTokenSource> _folderStreamCts = new();
    // Bounded set of recently-seen SSE event ids, used to drop duplicates when an opened folder
    // equals the server's default instance (both the default and the folder stream deliver the
    // same events, and double-applying part deltas would corrupt message text).
    private const int MaxSeenEventIds = 2000;
    private readonly HashSet<string> _seenEventIds = new();
    private readonly Queue<string> _seenEventIdOrder = new();
    private string _baseUrl = "";
    private string? _password;
    private string? _username;

    private bool _creatingSession;
    private bool _refreshingSessions;
    private bool _refreshSessionsQueued;

    // Cached per-session stores keyed by session id. The active session's store is always
    // registered here (a draft store is registered under its id the moment a new session is
    // created server-side). Stores are never created for sessions the user has not opened —
    // background events only feed the sidebar maps, not a message list.
    private readonly Dictionary<string, SessionStore> _sessionStores = new();

    private CancellationTokenSource? _toastCts;

    /// <summary>
    /// Raised after <see cref="Active"/> changes (session switch / new session / active
    /// deleted / configure reset). The chat page re-hooks the active store's message list.
    /// </summary>
    public event Action? ActiveStoreChanged;

    [QuickMarkupConstructor]
    private void Ctor()
    {
        Active = NewDraftStore();
        Init();
    }

    private SessionStore NewDraftStore()
    {
        var store = new SessionStore();
        store.Router = this;
        store.SessionId = "";
        return store;
    }

    private SessionStore NewCachedStore(string sessionId)
    {
        var store = new SessionStore();
        store.Router = this;
        store.SessionId = sessionId;
        _sessionStores[sessionId] = store;
        return store;
    }

    /// <summary>Returns a cached store for the session, or null when it was never opened.</summary>
    private SessionStore? GetStore(string sessionId) =>
        sessionId.Length == 0 ? null : _sessionStores.GetValueOrDefault(sessionId);

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
        ConnectionUrl = baseUrl;
        ConnectionPassword = (password ?? Environment.GetEnvironmentVariable(OpencodeClient.PasswordEnvVar)) ?? "";
        // Reset the display label unless a local serve process already set it (folder launch).
        if (ServeProcess is null) DisplayLabel = "";
        _client = new OpencodeClient(baseUrl, password, username);
        _started = false;
        _cts?.Cancel();
        _cts = null;
        _sessionStores.Clear();
        Active = NewDraftStore();
        ActiveSessionId = "";
        SubagentCount = 0;
        ActiveSubagents.Clear();
        _permissions.Clear();
        ActivePermission = null;
        Sessions.Clear();
        DirectoryGroups.Clear();
        _sessionStatus.Clear();
        _unread.Clear();
        _sessionOutcome.Clear();
        _pendingQuestions.Clear();
        _pendingPermissions.Clear();
        _directoryExpanded.Clear();
        _openedFolders.Clear();
        foreach (var cts in _folderStreamCts.Values) cts.Cancel();
        _folderStreamCts.Clear();
        _seenEventIds.Clear();
        _seenEventIdOrder.Clear();
        McpServers.Clear();
        _mcpDirectory = "";
        McpDirectory = "";
        McpSummary = "";
        _mcpBusy = false;
        _mcpPolling = false;
        ConnectionStatus = "Connecting...";
        DismissToast();
        ActiveStoreChanged?.Invoke();
    }

    /// <summary>
    /// Takes ownership of a locally-launched serve process. Disposes any previous one.
    /// </summary>
    public void AttachServeProcess(ServeProcess serve)
    {
        var old = ServeProcess;
        ServeProcess = serve;
        if (serve.WorkingDirectory.Length > 0)
            DisplayLabel = serve.WorkingDirectory;
        old?.Dispose();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts = null;
        foreach (var cts in _folderStreamCts.Values) cts.Cancel();
        _folderStreamCts.Clear();
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
                if (ServeProcess is not null)
                {
                    // Folder launch: use the folder we started serve in.
                    ServerDirectory = ServeProcess.WorkingDirectory;
                }
                else
                {
                    // URL connection: fetch the server's default directory.
                    var dir = await _client.GetDirectoryAsync(ct);
                    if (dir is { Length: > 0 })
                    {
                        ServerDirectory = dir;
                        DisplayLabel = $"{_baseUrl} - {dir}";
                    }
                }
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

    /// <summary>
    /// Ensures the active session exists server-side, creating it lazily on the first send.
    /// The draft store is upgraded in place: its SessionId is set and it is registered in the
    /// per-session cache, so the messages the user already sees stay attached to it.
    /// </summary>
    public async Task<bool> EnsureSessionAsync()
    {
        if (Active.SessionId.Length > 0) return true;
        while (_creatingSession) await Task.Delay(10);
        if (Active.SessionId.Length > 0) return true;

        _creatingSession = true;
        try
        {
            // Lazy session creation: no title is passed (null) so the server assigns a
            // timestamped default title and auto-generates a name on the first prompt.
            var id = await _client.CreateSessionAsync(null, _pendingDirectory, Active.Mode, Active.ProviderId, Active.ModelId, Active.Variant) ?? "";
            _pendingDirectory = null;
            if (id.Length > 0)
            {
                Active.SessionId = id;
                _sessionStores[id] = Active;
            }
        }
        finally
        {
            _creatingSession = false;
        }

        if (Active.SessionId.Length == 0)
        {
            ConnectionStatus = "Error: could not create session";
            return false;
        }
        Active.SessionTitle = "New Chat";
        ActiveSessionId = Active.SessionId;
        await RefreshSessionsAsync();
        await RefreshMcpStatusAsync();
        return true;
    }

    public async Task RefreshSessionsAsync(CancellationToken ct = default)
    {
        // Coalesce concurrent refreshes (e.g. NewSessionAsync's background refresh racing
        // EnsureSessionAsync's post-create refresh): if one is in flight, queue a follow-up
        // so the newest session list still lands.
        if (_refreshingSessions)
        {
            _refreshSessionsQueued = true;
            return;
        }
        _refreshingSessions = true;
        try
        {
            do
            {
                _refreshSessionsQueued = false;
                await RefreshSessionsCoreAsync(ct);
            }
            while (_refreshSessionsQueued);
        }
        finally
        {
            _refreshingSessions = false;
        }
    }

    private async Task RefreshSessionsCoreAsync(CancellationToken ct)
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

            // The default GET /session list is scoped to the server's default project
            // (instance). Folders opened via the sidebar's Open Folder button live in their
            // own instance, so their sessions must be fetched per-directory and merged in —
            // otherwise a picked folder's chats never appear in the sidebar.
            foreach (var dir in _openedFolders.Keys.ToList())
            {
                try
                {
                    var extra = await _client.ListSessionsAsync(ct, dir);
                    foreach (var session in extra)
                    {
                        if (Sessions.Any(s => s.Id == session.Id)) continue;
                        ApplySessionFlags(session);
                        Sessions.Add(session);
                    }
                }
                catch
                {
                    // One unreachable/unlisted folder must not break the whole refresh.
                }
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
        var sessionId = Active.SessionId;
        if (sessionId.Length > 0)
        {
            var session = Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session is not null && session.Directory.Length > 0) return session.Directory;
        }
        return _pendingDirectory ?? "";
    }

    /// <summary>True when a session is mid-turn: its cached store says busy, or the router's status map says so.</summary>
    public bool IsSessionBusy(string sessionId)
    {
        if (GetStore(sessionId) is { } store && store.IsBusy) return true;
        return _sessionStatus.GetValueOrDefault(sessionId) is not (null or "idle");
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
    /// Rebuilds the sidebar's directory grouping from <see cref="Sessions"/>, then merges in any
    /// folders opened via the "Open Folder" button that have no sessions yet, so a picked folder
    /// shows up immediately (even before the server has created a session in it). Subagent sessions
    /// (those spawned by a <c>task</c> tool call, identified by a non-empty ParentId) are kept in
    /// <see cref="Sessions"/> but filtered out of the sidebar — they're opened via their clickable
    /// task tool card instead, mirroring the TUI which hides them from session lists.
    /// </summary>
    private void RebuildDirectoryGroups()
    {
        var groups = new List<DirectoryGroup>();
        var sortKey = new Dictionary<string, long>();

        foreach (var g in Sessions
            .Where(s => !s.IsSubagent)
            .GroupBy(s => s.Directory))
        {
            var dir = g.Key.Length == 0 ? "(unknown)" : g.Key;
            var sessions = g.OrderByDescending(s => s.Updated).ToList();
            sortKey[dir] = sessions.Count > 0 ? sessions[0].Updated : 0;
            groups.Add(new DirectoryGroup
            {
                Directory = dir,
                Sessions = new ObservableCollection<SessionInfo>(sessions),
            });
        }

        // Folders opened via the sidebar's Open Folder button (or a group "+" button) appear even
        // with zero sessions; sort them by when they were last opened.
        foreach (var (dir, opened) in _openedFolders)
        {
            if (sortKey.ContainsKey(dir)) continue;
            sortKey[dir] = opened;
            groups.Add(new DirectoryGroup
            {
                Directory = dir,
                Sessions = new ObservableCollection<SessionInfo>(),
            });
        }

        DirectoryGroups.Clear();
        foreach (var group in groups.OrderByDescending(g => sortKey[g.Directory]))
        {
            // Re-apply the user's show-more/show-less choice; the toggle mutates the item's
            // reactive IsExpanded in place, while a rebuild gets its value from the store map.
            group.IsExpanded = _directoryExpanded.GetValueOrDefault(group.Directory);
            DirectoryGroups.Add(group);
        }
    }

    /// <summary>
    /// Records a folder as opened via the sidebar, so it shows up even with no sessions yet,
    /// and opens an /event stream scoped to it.
    /// </summary>
    private void RegisterOpenedFolder(string directory)
    {
        var path = directory.Trim();
        while (path.Length > 1 && path.EndsWith('/')) path = path[..^1];
        if (path.Length == 0) return;
        _openedFolders[path] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        StartFolderEventStream(path);
    }

    /// <summary>
    /// Opens an /event stream scoped to <paramref name="directory"/> via ?directory= so the app
    /// receives live events (message parts, status) for sessions in a picked folder. The app's
    /// main /event stream is scoped to the server's default instance, which filters out events
    /// from other directories — without this, a session created in an opened folder never updates
    /// the chat until the user switches away and back. Tied to the connect lifetime and cancelled
    /// on Configure/Dispose.
    /// </summary>
    private void StartFolderEventStream(string directory)
    {
        if (_client is null || _folderStreamCts.ContainsKey(directory)) return;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts?.Token ?? CancellationToken.None);
        _folderStreamCts[directory] = cts;
        var url = $"{_client.BaseUrl}/event?directory={Uri.EscapeDataString(directory)}";
        _ = Task.Run(() => EventStreamReader.ReadAsync(_client.Http, url, _events.Writer, cts.Token));
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
        var sessionId = Active.SessionId;
        if (sessionId.Length == 0)
        {
            SubagentCount = 0;
            return;
        }
        foreach (var session in Sessions
            .Where(s => s.ParentId == sessionId)
            .OrderByDescending(s => s.Updated))
        {
            ActiveSubagents.Add(session);
        }
        SubagentCount = ActiveSubagents.Count;
    }

    /// <summary>
    /// Applies a <c>session.created</c>/<c>session.updated</c> event. Keeps the sidebar and the
    /// active session header in sync when the server renames a session (e.g. the title agent
    /// replaces a default title with a generated one), and forwards the info to the session's
    /// cached store (title/parent/model/revert marker).
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

        // Keep any cached store (the active one included) in sync: title renames, the subagent
        // parent link, model settings, and the revert marker (the server omits "revert" on unrevert).
        GetStore(session.Id)?.ApplySessionInfo(session, info);
    }

    /// <summary>
    /// Applies a <c>session.deleted</c> event: removes the session from the sidebar and the
    /// store cache immediately, and clears the active view if the deleted session was active.
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

        _sessionStores.Remove(id);

        if (id != Active.SessionId) return;

        // The active session was deleted; fall back to an empty state.
        Active = NewDraftStore();
        ActiveSessionId = "";
        RebuildActiveSubagents();
        ActiveStoreChanged?.Invoke();
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

    /// <summary>
    /// Refreshes the shared mode/model option lists and re-applies the active session's
    /// selections (used as defaults for a new draft chat).
    /// </summary>
    public async Task RefreshSettingsAsync(CancellationToken ct = default)
    {
        try
        {
            var modes = await _client.GetModesAsync(ct);
            ModeOptions.Clear();
            foreach (var mode in modes) ModeOptions.Add(mode);
            if (Active.Mode.Length == 0 || !ModeOptions.Contains(Active.Mode)) Active.Mode = "build";

            var models = await _client.GetModelsAsync(ct);
            ModelOptions.Clear();
            foreach (var model in models) ModelOptions.Add(model);

            // Prefer a root session (not a subagent) when guessing the model for new chats.
            var known = Sessions.FirstOrDefault(s => !s.IsSubagent && s.ModelId.Length > 0 && ModelOptions.Any(m => m.Id == s.ModelId));
            if (known is not null)
            {
                Active.ModelId = known.ModelId;
                if (known.ModelProviderId.Length > 0) Active.ProviderId = known.ModelProviderId;
            }
            Active.UpdateVariantOptions();
            Active.ReapplyComboSelections();
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Starts a new (unsaved) chat. The session is created lazily on the first message send
    /// (<see cref="EnsureSessionAsync"/>), so clicking "+" doesn't produce an empty server-side
    /// session. <paramref name="directory"/> is remembered for that deferred creation.
    /// </summary>
    public Task NewSessionAsync(string? directory = null)
    {
        if (!string.IsNullOrEmpty(directory)) RegisterOpenedFolder(directory);
        _pendingDirectory = directory;
        Active = NewDraftStore();
        ActiveSessionId = "";
        RebuildActiveSubagents();
        ActiveStoreChanged?.Invoke();
        _permissions.Clear();
        ActivePermission = null;
        DismissToast();
        // Show the picked folder immediately (even with zero sessions — RebuildDirectoryGroups
        // merges opened folders in), then background-refresh so any existing sessions in it are
        // fetched from the server (the default GET /session list excludes other instances).
        RebuildDirectoryGroups();
        _ = RefreshSessionsAsync();
        _ = RefreshMcpStatusAsync();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Switches the active view to a session. A store is created and loaded the first time the
    /// session is opened and then cached, so switching away and back reuses the live message
    /// list (stale-while-revalidate refreshes it in the background when not mid-turn).
    /// </summary>
    public async Task SwitchSessionAsync(string sessionId)
    {
        if (sessionId.Length == 0 || sessionId == Active.SessionId) return;

        var known = Sessions.FirstOrDefault(s => s.Id == sessionId);
        var cached = GetStore(sessionId);
        var store = cached ?? NewCachedStore(sessionId);

        Active = store;
        ActiveSessionId = sessionId;
        ActiveStoreChanged?.Invoke();

        // Seed busy from the router's status map (a session already mid-turn before we opened
        // it — the server only emits status on transitions).
        if (store.SessionId.Length > 0) store.IsBusy = IsSessionBusy(sessionId);

        // Viewing the session now; clear any unread marker for it.
        _unread[sessionId] = false;
        if (known is not null) known.IsUnread = false;

        RebuildActiveSubagents();

        if (cached is not null)
        {
            // Stale-while-revalidate: re-fetch the cached store's messages in the background so a
            // revisit shows fresh content (skipped while busy so an in-flight turn's streaming
            // deltas are never clobbered by a snapshot taken mid-stream).
            _ = store.RefreshAsync();
        }
        else
        {
            try
            {
                await store.LoadAsync(known);
            }
            catch (Exception ex)
            {
                ConnectionStatus = $"Error: {ex.Message}";
            }
        }

        await SyncPendingQuestionsAsync();
        await SyncPendingPermissionsAsync();
        await RefreshMcpStatusAsync();
    }

    /// <summary>
    /// Returns to the parent session of the currently-active subagent session. No-op for
    /// root sessions. Mirrors the TUI's "go to parent session" navigation.
    /// </summary>
    public async Task GoToParentAsync()
    {
        if (Active.ParentSessionId.Length == 0) return;
        await SwitchSessionAsync(Active.ParentSessionId);
    }

    /// <summary>
    /// Forks the conversation at a specific message (TUI/web parity: "Fork" action). Calls
    /// POST /session/{id}/fork with the target message id — the server creates a new session
    /// containing all messages strictly before the fork point (the forked-at message itself is
    /// excluded) titled "&lt;original&gt; (fork #N)" — then switches to it and restores the
    /// forked-at message's prompt (text + staged images) into the composer so the user can
    /// continue from there. Returns the new session id, or null on failure/no session.
    /// </summary>
    public async Task<string?> ForkFromMessageAsync(MessageItem message)
    {
        if (Active.SessionId.Length == 0 || message is null) return null;
        try
        {
            var forked = await _client.ForkSessionAsync(Active.SessionId, message.Id);
            if (forked is null || forked.Id.Length == 0) return null;

            await SwitchSessionAsync(forked.Id);

            Active.ForkPromptText = SessionStore.PromptTextFromMessage(message);
            Active.StageImagesFromMessage(message);
            return forked.Id;
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// Forks the whole active session (TUI/web parity: "Full session" fork). Calls
    /// POST /session/{id}/fork with no message id so the server copies every message and titles
    /// the new session "&lt;original&gt; (fork #N)", then switches to it. Unlike the per-message
    /// fork there's no prompt to restore — the composer keeps whatever the user had. Returns the
    /// new session id, or null on failure/no session.
    /// </summary>
    public async Task<string?> ForkFullSessionAsync()
    {
        if (Active.SessionId.Length == 0) return null;
        try
        {
            var forked = await _client.ForkSessionAsync(Active.SessionId);
            if (forked is null || forked.Id.Length == 0) return null;

            await SwitchSessionAsync(forked.Id);
            return forked.Id;
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
            return null;
        }
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
            while (reader.TryRead(out var evt))
            {
                if (IsDuplicateEvent(evt)) continue;
                batch.Add(evt);
            }

            _dispatcher?.TryEnqueue(() =>
            {
                foreach (var evt in batch) Apply(evt);
            });
        }
    }

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

    /// <summary>
    /// Applies a single SSE event. Router-level events (session CRUD, status, permissions,
    /// MCP, toasts) are handled here; session-scoped message/question events are dispatched to
    /// the owning session's cached store (no-op when the session was never opened — only the
    /// sidebar maps are fed, and there is no message list to mutate).
    /// </summary>
    private void Apply(OpencodeEvent evt)
    {
        switch (evt.Type)
        {
            case "message.updated":
            {
                var sessionId = evt.Properties.GetStringProperty("sessionID");
                // Feed the sidebar outcome tracker for assistant message completions. The last
                // update for a turn carries its definitive outcome (error/finish/cost/tokens).
                if (evt.Properties.TryGetProperty("info", out var info)
                    && info.GetStringProperty("role") == "assistant")
                    _sessionOutcome[sessionId] = SessionStore.ClassifyMessageOutcome(info);
                GetStore(sessionId)?.ApplyMessageUpdated(evt.Properties);
                break;
            }
            case "message.part.updated":
                DispatchToSession(evt.Properties, static (s, p) => s.ApplyPartUpdated(p));
                break;
            case "message.part.delta":
                DispatchToSession(evt.Properties, static (s, p) => s.ApplyPartDelta(p));
                break;
            case "message.part.removed":
                DispatchToSession(evt.Properties, static (s, p) => s.ApplyPartRemoved(p));
                break;
            case "message.removed":
                DispatchToSession(evt.Properties, static (s, p) => s.ApplyMessageRemoved(p));
                break;
            case "session.status":
                ApplySessionStatus(evt.Properties);
                break;

            // Questions
            case "question.asked":
                ApplyQuestionAsked(evt.Properties);
                break;
            case "question.replied":
            case "question.rejected":
                ApplyQuestionReplied(evt.Properties);
                break;

            // Permissions (intentionally NOT filtered by session: subagents run in their own
            // sessions, and a pending subagent permission would otherwise hang forever).
            case "permission.asked":
                ApplyPermissionAsked(evt.Properties);
                break;
            case "permission.replied":
                ApplyPermissionReplied(evt.Properties);
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

    /// <summary>Dispatches a session-scoped event to that session's cached store, if any.</summary>
    private void DispatchToSession(JsonElement properties, Action<SessionStore, JsonElement> apply)
    {
        var sessionId = properties.GetStringProperty("sessionID");
        if (sessionId.Length == 0) return;
        if (_sessionStores.TryGetValue(sessionId, out var store)) apply(store, properties);
    }

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

    /// <summary>
    /// Applies a <c>session.status</c> event: tracks the busy/unread/outcome sidebar indicators
    /// for every session, and forwards it to the session's cached store for its banner,
    /// retry card and Continue-button state.
    /// </summary>
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
                if (type == "idle" && sessionId != Active.SessionId)
                {
                    _unread[sessionId] = true;
                    item.IsUnread = true;
                    item.Outcome = _sessionOutcome.GetValueOrDefault(sessionId) ?? "";
                }
            }
        }

        // The active-session banner (IsBusy/StatusMessage) only applies to the session's store.
        GetStore(sessionId)?.ApplySessionStatus(properties);
    }

    private void ApplyQuestionAsked(JsonElement properties)
    {
        var requestId = properties.GetStringProperty("id");
        if (requestId.Length == 0) return;

        // Track the pending question per session for the sidebar attention indicator.
        var sessionId = properties.GetStringProperty("sessionID");
        if (sessionId.Length > 0)
        {
            _pendingQuestions[sessionId] = _pendingQuestions.GetValueOrDefault(sessionId) + 1;
            var item = Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (item is not null) ApplySessionFlags(item);
        }

        // Attach the live question to the session's store (active or cached).
        GetStore(sessionId)?.ApplyQuestionAsked(properties);
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
    /// counts (drives the sidebar attention indicator) and re-attaches requestIDs to each
    /// cached session store's tool parts after a reload (requestIDs only exist in the live
    /// question.asked event and the server's in-memory pending map, not in the persisted
    /// message parts).
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

                GetStore(sessionId)?.AttachQuestionRequest(question);
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
                if (Active.SessionId.Length == 0) continue;
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

    public async Task RejectQuestionAsync(string requestId)
    {
        try
        {
            await _client.RejectQuestionAsync(requestId);
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
        }
    }
}
