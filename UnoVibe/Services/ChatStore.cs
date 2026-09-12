using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using UnoVibe.Models;
using static UnoVibe.Integration.ResultExtension;
using OpencodeClient = UnoVibe.Integration.OpencodeClient;
using OpencodeEvent = UnoVibe.Integration.OpencodeEvent;
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
    // Number of subagent sessions belonging to the active session (ParentId == active session id).
    // Drives the chat page's subagent strip and the flyout's "Tokens (excludes subagents)" label.
    public int SubagentCount;
    // The permission request currently shown to the user (oldest pending), or null.
    public PermissionRequestItem? ActivePermission;
    public ToastItem? CurrentToast;
    // The store for the currently-open session (see the class doc).
    // it starts as an unsaved draft and is re-pointed on switch/new/delete.
    public SessionStore Active = `NewDraftStore()`;
    """)]
public sealed partial class ChatStore : IDisposable
{
    public ObservableCollection<string> ModeOptions { get; } = new();
    public ObservableCollection<ModelOption> ModelOptions { get; } = new();
    public ObservableCollection<string> VariantOptions { get; } = new();

    public ObservableCollection<SessionInfoToRemove> SessionsToRemove { get; } = new();
    // Subagent sessions (ParentId == active session id), in display order for the chat page
    // subagent strip. Rebuilt via ReconcileActiveSubagents whenever the session list or the
    // active session changes.
    public ObservableCollection<SessionInfoToRemove> ActiveSubagents { get; } = new();

    /// <summary>
    /// The window this store belongs to (set by <see cref="WindowController"/>). Used by the native
    /// toast notifications' focus gate, which must only consider THIS window's foreground state —
    /// a second window being focused must not suppress this window's active-session toasts.
    /// </summary>
    public Window? OwnerWindow { get; set; }

    /// <summary>The HTTP client for the configured server (null before <see cref="Configure"/>).</summary>
    public OpencodeClient? Client => _client;

    private OpencodeClient _client = null!;
    private readonly Channel<OpencodeEvent> _events = Channel.CreateUnbounded<OpencodeEvent>();
    // Per-session sidebar state (busy status, unread, last-turn outcome, pending question/approval
    // counts) keyed by session id. Survives session list refreshes — ApplySessionFlags seeds each
    // SessionInfo from the owning entry. One map of SessionFlags instead of five parallel dicts.
    private readonly Dictionary<string, SessionFlags> _sessionFlags = new();
    // Sidebar directory groups keyed by directory. Groups are reused (never recreated) across
    // session refreshes, so per-group state — IsExpanded (show more/less) and Branch (git branch)
    // — lives on the DirectoryGroup instance itself instead of separate per-directory maps.
    private readonly Dictionary<string, DirectoryGroup> _groupsByDirectory = new();
    // O(1) lookup index for Sessions by id, kept in sync with the ObservableCollection so
    // SSE handlers and helpers never scan the list linearly (GetSession).
    private readonly Dictionary<string, SessionInfoToRemove> _sessionsById = new();
    private readonly List<PermissionRequestItem> _permissions = new();
    // Pending question requestID → owning workspace directory (see PermissionDirectory for the
    // same per-instance contract). Seeded from question.asked events and the /question list so a
    // reply/reject reaches the instance holding the pending question (folder-opened sessions live
    // in a non-default instance — a directory-less reply would 404).
    private readonly Dictionary<string, string> _questionDirectories = new();
    private CancellationTokenSource? _cts;
    private DispatcherQueue? _dispatcher;
    private bool _started;
    private string? _pendingDirectory;
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

    private bool _creatingSession;
    private bool _refreshingSessions;
    private bool _refreshSessionsQueued;

    // Cached per-session stores keyed by session id. The active session's store is always
    // registered here (a draft store is registered under its id the moment a new session is
    // created server-side). Stores are never created for sessions the user has not opened —
    // background events only feed the sidebar maps, not a message list.
    private readonly Dictionary<string, SessionStore> _sessionStores = new();

    /// <summary>
    /// Raised after <see cref="Active"/> changes (session switch / new session / active
    /// deleted / configure reset). The chat page re-hooks the active store's message list.
    /// </summary>
    public event Action? ActiveStoreChanged;

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

    /// <summary>The sidebar session with the given id, or null when not listed.</summary>
    public SessionInfoToRemove? GetSession(string sessionId) =>
        sessionId.Length == 0 ? null : _sessionsById.GetValueOrDefault(sessionId);

    /// <summary>Gets or creates the per-session sidebar state entry for <paramref name="sessionId"/> (non-empty).</summary>
    private SessionFlags Flags(string sessionId) =>
        _sessionFlags.TryGetValue(sessionId, out var flags)
            ? flags
            : _sessionFlags[sessionId] = new SessionFlags();

    /// <summary>
    /// Configures the server to connect to. Must be called before <see cref="ConnectAsync"/>.
    /// </summary>
    public void Configure()
    {
        _started = false;
        _cts?.Cancel();
        _cts = null;
        _sessionStores.Clear();
        Active = NewDraftStore();
        SubagentCount = 0;
        ActiveSubagents.Clear();
        _permissions.Clear();
        ActivePermission = null;
        _questionDirectories.Clear();
        SessionsToRemove.Clear();
        _sessionsById.Clear();
        
        _groupsByDirectory.Clear();
        _sessionFlags.Clear();
        foreach (var cts in _folderStreamCts.Values) cts.Cancel();
        _folderStreamCts.Clear();
        _seenEventIds.Clear();
        _seenEventIdOrder.Clear();
        
        ActiveStoreChanged?.Invoke();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts = null;
        foreach (var cts in _folderStreamCts.Values) cts.Cancel();
        _folderStreamCts.Clear();
    }

    public async Task ConnectAsync()
    {
        if (_started) return;
        if (_client is null)
        {
            return;
        }
        _started = true;

        var cts = new CancellationTokenSource();
        _cts = cts;
        var ct = cts.Token;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _ = Task.Run(() => _client.ReadEventAsync(_events.Writer, ct));
        _ = Task.Run(() => PumpAsync(ct));

        

        await RefreshSessionsAsync(ct);
        await RefreshSessionStatusAsync(ct);
        await SyncPendingPermissionsAsync();
        await SyncPendingQuestionsAsync();
        await RefreshModelsAsync(ct);
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
            if (!(await _client.CreateSessionAsync(new()
            {
                Title = null,
                Agent = Active.Mode,
                Model = new()
                {
                    ProviderID = Active.ProviderId,
                    Id = Active.ModelId,
                    Variant = Active.Variant
                },
            }, _pendingDirectory)).TryGetValue(out var session, out var error))
            {
                ShowError(error, "Could not create session");
            }
            _pendingDirectory = null;
            if (session.Id.Length > 0)
            {
                Active.SessionId = session.Id;
                _sessionStores[session.Id] = Active;
            }
        }
        finally
        {
            _creatingSession = false;
        }

        if (Active.SessionId.Length == 0)
        {
            ShowError("Could not create session");
            return false;
        }
        Active.SessionTitle = "New Chat";
        ActiveSessionId = Active.SessionId;
        await RefreshSessionsAsync();
        return true;
    }

    /// <summary>
    /// Polls GET /session/status for the currently-busy sessions. The server only emits
    /// session.status SSE events on transitions, so a session already mid-turn before we
    /// connected would otherwise never show as busy.
    /// </summary>
    public async Task RefreshSessionStatusAsync(CancellationToken ct = default)
    {
        // Reset every session's Status first, then re-apply the server's poll — the other
        // flag fields (unread/outcome/counters) must survive this refresh.
        foreach (var flags in _sessionFlags.Values) flags.Status = null;
        if (!(await _client.GetSessionStatusAsync(ct)).TryGetValue(out var statuses, out var error))
        {
            ShowError(error, "Could not refresh session status");
            return;
        }
        foreach (var kv in statuses) Flags(kv.Key).Status = kv.Value.Type;
        foreach (var s in SessionsToRemove) ApplySessionFlags(s);
    }

    /// <summary>The directory used for instance-scoped MCP queries: the active session's, else the pending/current one.</summary>
    public string ActiveDirectory()
    {
        var sessionId = Active.SessionId;
        if (sessionId.Length > 0)
        {
            var session = GetSession(sessionId);
            if (session is not null && session.Directory.Length > 0) return session.Directory;
        }
        return _pendingDirectory ?? "";
    }

    /// <summary>True when a session is mid-turn: its cached store says busy, or the router's status flags say so.</summary>
    public bool IsSessionBusy(string sessionId)
    {
        if (GetStore(sessionId) is { } store && store.Head.IsBusy) return true;
        return _sessionFlags.GetValueOrDefault(sessionId)?.Status is not (null or "idle");
    }

    /// <summary>Copies the reactive busy/outcome/attention flags from the session's state entry onto a session item.</summary>
    private void ApplySessionFlags(SessionInfoToRemove session)
    {
        var flags = _sessionFlags.GetValueOrDefault(session.Id);
        session.Head.IsBusy = flags?.Status is not (null or "idle");
        session.Head.Outcome = flags?.Outcome ?? ChatOutcome.None;
        session.Head.IsPendingQuestion = (flags?.PendingQuestions ?? 0) > 0;
        session.Head.IsPendingPermission = (flags?.PendingPermissions ?? 0) > 0;
    }

    /// <summary>Re-applies the reactive per-session flags to every sidebar item (after counters change).</summary>
    private void RefreshSessionFlags()
    {
        foreach (var s in SessionsToRemove) ApplySessionFlags(s);
    }

    /// <summary>
    /// Fetches the git branch (<c>GET /vcs</c>) for every sidebar directory group and updates
    /// the group's reactive <c>Branch</c> in place (no group rebuild needed). Called on connect
    /// and on <c>vcs.branch.updated</c> events; failures keep the previously-known branch.
    /// </summary>
    public void RefreshBranches()
    {
        if (_client is null) return;
        var directories = _groupsByDirectory.Keys
            .Where(d => d.Length > 0 && d != "(unknown)")
            .ToList();
        if (directories.Count == 0) return;
        _ = RefreshBranchesCoreAsync(directories);
    }

    private async Task RefreshBranchesCoreAsync(List<string> directories)
    {
        foreach (var directory in directories)
        {
            if (_groupsByDirectory.TryGetValue(directory, out var group))
            {
                if (!(await _client.GetVCSInfoAsync(directory)).TryGetValue(out var vcs, out var error))
                {
                    ShowError(error, $"Could not read VCS branch for directory {directory}");
                    continue;
                }
                group.Branch = vcs.Branch ?? "";
            }
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
        _ = Task.Run(() => _client.ReadEventAsync(_events.Writer, cts.Token, directory));
    }

    /// <summary>
    /// Expands/collapses a sidebar directory group (show all sessions vs. a capped preview).
    /// The state lives on the group's reactive <c>IsExpanded</c>, which survives reconciles
    /// because group instances are reused.
    /// </summary>
    public void ToggleDirectoryExpanded(string directory)
    {
        if (_groupsByDirectory.TryGetValue(directory, out var group))
            group.IsExpanded = !group.IsExpanded;
    }

    /// <summary>
    /// Reconciles <see cref="ActiveSubagents"/> (subagent sessions whose parent is the active
    /// session) in place and updates the reactive <see cref="SubagentCount"/>. Subagents are
    /// hidden from the sidebar, so this collection is the chat page's way to list them. Items
    /// are the same persistent SessionInfo instances as <see cref="SessionsToRemove"/>, so survivors
    /// keep their live state; only missing/added/reordered ones change.
    /// </summary>
    private void ReconcileActiveSubagents()
    {
        var sessionId = Active?.Head.Id;
        var desired = sessionId is null
            ? new List<SessionInfoToRemove>()
            : SessionsToRemove.Where(s => s.Head.ParentId == sessionId).OrderByDescending(s => s.Head.Updated).ToList();
        ReconcileSessionCollection(ActiveSubagents, desired);
        SubagentCount = ActiveSubagents.Count;
    }

    /// <summary>
    /// Reconciles an observable session list in place against the desired (sorted) set, using
    /// reference identity — the items are shared persistent SessionInfo instances, so surviving
    /// entries keep their live reactive state. Removes items not in <paramref name="desired"/>,
    /// inserts new ones, and moves the rest to match <paramref name="desired"/>'s order.
    /// </summary>
    private static void ReconcileSessionCollection(ObservableCollection<SessionInfoToRemove> items, List<SessionInfoToRemove> desired)
    {
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(items[i])) items.RemoveAt(i);
        }

        var index = 0;
        foreach (var session in desired)
        {
            var current = items.IndexOf(session);
            if (current < 0) items.Insert(index, session);
            else if (current != index) items.Move(current, index);
            index++;
        }
    }

    /// <summary>
    /// Applies a <c>session.created</c>/<c>session.updated</c> event. Keeps the sidebar and the
    /// active session header in sync when the server renames a session (e.g. the title agent
    /// replaces a default title with a generated one), and forwards the info to the session's
    /// cached store (title/parent/model/revert marker).
    /// </summary>
    private void ApplySessionUpsert(JsonElement properties)
    {
        // Keep any cached store (the active one included) in sync: title renames, the subagent
        // parent link, model settings, and the revert marker (the server omits "revert" on unrevert).
        GetStore(session.Id)?.ApplySessionInfo(SessionInfoToRemove.From(session), info);
    }

    private static Integration.SessionInfo SessionInfoFromJson(JsonElement item)
        => item.Deserialize(AppJsonContext.Default.SessionInfo)!;

    /// <summary>
    /// Switches the active view to a session. A store is created and loaded the first time the
    /// session is opened and then cached, so switching away and back reuses the live message
    /// list (stale-while-revalidate refreshes it in the background when not mid-turn).
    /// </summary>
    public async Task SwitchSessionAsync(string sessionId)
    {
        if (sessionId.Length == 0 || sessionId == Active.SessionId) return;

        var known = GetSession(sessionId);
        var cached = GetStore(sessionId);
        var store = cached ?? NewCachedStore(sessionId);

        Active = store;
        ActiveSessionId = sessionId;
        ActiveStoreChanged?.Invoke();

        // Seed busy from the router's status map (a session already mid-turn before we opened
        // it — the server only emits status on transitions).
        if (store.SessionId.Length > 0) store.IsBusy = IsSessionBusy(sessionId);

        // Viewing the session now; mark it read so the sidebar indicator is suppressed.
        known?.Head.IsRead = true;

        ReconcileActiveSubagents();

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
                ShowError(ex.Message, "Could not open session");
            }
        }

        await SyncPendingQuestionsAsync();
        await SyncPendingPermissionsAsync();
        await RefreshMcpStatusAsync();
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
                ApplySessionStatus(evt.Properties); // DONE, action needed in session store
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
                ApplySessionUpsert(evt.Properties); // DONE
                break;
            case "session.deleted":
                ApplySessionDeleted(evt.Properties); // DONE
                break;

            // Files / project / VCS
            case "vcs.branch.updated":
                // The git branch changed in a workspace. The payload only carries { branch }
                // (no directory), so refresh every sidebar directory group's branch label.
                RefreshBranches();
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

    /// <summary>
    /// Applies a <c>session.status</c> event: tracks the busy/unread/outcome sidebar indicators
    /// for every session, and forwards it to the session's cached store for its banner,
    /// retry card and Continue-button state.
    /// </summary>
    private void ApplySessionStatus(JsonElement properties)
    {
        if (!properties.TryGetProperty("status", out var status)) return;
        var type = status.GetStringProperty("type");

        var sessionId = properties.GetStringProperty("sessionID");
        var store = sessionId.Length > 0 ? GetStore(sessionId) : null;

        // The active-session banner (IsBusy/StatusMessage) only applies to the session's store.
        store?.ApplySessionStatus(properties);
    }

    private void ApplyQuestionAsked(JsonElement properties)
    {
        var requestId = properties.GetStringProperty("id");
        if (requestId.Length == 0) return;

        // Track the pending question per session for the sidebar attention indicator.
        var sessionId = properties.GetStringProperty("sessionID");
        if (sessionId.Length > 0)
        {
            Flags(sessionId).PendingQuestions++;
            var item = GetSession(sessionId);
            if (item is not null) ApplySessionFlags(item);
            // Native toast: suppressed only for the active session while its OWNING window is focused
            // (the inline question form is already on screen there).
            if (item is not null)
                Notifications.NotifyQuestion(OwnerWindow, item, FirstQuestionText(properties),
                    sessionId == Active.SessionId);
            _questionDirectories[requestId] = DirectoryOf(sessionId);
        }

        // Attach the live question to the session's store (active or cached).
        GetStore(sessionId)?.ApplyQuestionAsked(properties);
    }

    /// <summary>First question's text from a <c>question.asked</c> payload (for notification text).</summary>
    private static string FirstQuestionText(JsonElement properties)
    {
        if (!properties.TryGetProperty("questions", out var questions) || questions.ValueKind != JsonValueKind.Array)
            return "";
        foreach (var q in questions.EnumerateArray())
        {
            var text = q.GetStringProperty("question");
            if (text.Length > 0) return text;
        }
        return "";
    }

    /// <summary>Clears a session's pending-question count when a question is answered or dismissed.</summary>
    private void ApplyQuestionReplied(JsonElement properties)
    {
        var sessionId = properties.GetStringProperty("sessionID");
        if (sessionId.Length == 0) return;
        var flags = _sessionFlags.GetValueOrDefault(sessionId);
        if (flags is not null && flags.PendingQuestions > 0) flags.PendingQuestions--;
        var item = GetSession(sessionId);
        if (item is not null) ApplySessionFlags(item);

        var requestId = properties.GetStringProperty("requestID");
        if (requestId.Length > 0) _questionDirectories.Remove(requestId);
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
            Flags(sessionId).PendingPermissions++;
            var item = GetSession(sessionId);
            if (item is not null) ApplySessionFlags(item);
        }

        var request = PermissionRequestItem.FromJson(properties);
        // Native toast for a newly-arrived request. Suppressed for the active session (or a task
        // child of it) while its OWNING window is focused — the approval dialog is already on
        // screen there; a background session's approval always toasts.
        if (!_permissions.Any(p => p.Id == request.Id) && sessionId.Length > 0 && GetSession(sessionId) is { } pending)
            Notifications.NotifyPermission(OwnerWindow, pending, request.Title, request.Body,
                IsActiveOrDescendant(request.SessionId));
        AddPermissionRequest(request);
    }

    private void ApplyPermissionReplied(JsonElement properties)
    {
        var requestId = properties.GetStringProperty("requestID");
        if (requestId.Length > 0) RemovePermissionRequest(requestId);

        var sessionId = properties.GetStringProperty("sessionID");
        if (sessionId.Length > 0)
        {
            var flags = _sessionFlags.GetValueOrDefault(sessionId);
            if (flags is not null && flags.PendingPermissions > 0) flags.PendingPermissions--;
            var item = GetSession(sessionId);
            if (item is not null) ApplySessionFlags(item);
        }
    }

    public void AddPermissionRequest(PermissionRequestItem request)
    {
        if (_permissions.Any(p => p.Id == request.Id)) return;
        if (request.SessionId.Length > 0 && !IsActiveOrDescendant(request.SessionId)) return;
        _permissions.Add(request);
        UpdateActivePermission();
    }

    /// <summary>
    /// True when <paramref name="sessionId"/> is the active session or a descendant of it
    /// (a subagent reached via the parent chain), so a pending permission for a task child
    /// surfaces in the parent's dialog instead of being dropped.
    /// </summary>
    private bool IsActiveOrDescendant(string sessionId)
    {
        var current = sessionId;
        var guard = 0;
        while (current.Length > 0 && guard++ < 64)
        {
            if (current == Active.SessionId) return true;
            var info = GetSession(current);
            current = info?.ParentId ?? "";
        }
        return false;
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
        var directory = PermissionDirectory(requestId);
        try
        {
            await _client.ReplyPermissionAsync(requestId, new()
            {
                Reply = reply,
                Message = message
            }, directory);
            RemovePermissionRequest(requestId);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // The request is gone server-side without a permission.replied event (answered
            // elsewhere, or its turn/instance was aborted/disposed) — drop the stale card so the
            // next pending request can surface instead of a dead 404 dialog.
            RemovePermissionRequest(requestId);
            ShowWarning("The approval card was dismissed — the request is no longer pending.", "Permission already handled");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message, "Approval reply failed");
        }
    }

    /// <summary>
    /// Resolves the workspace directory that owns a pending request, so a reply reaches the
    /// instance holding it (folder-opened sessions live in a non-default instance). Falls back to
    /// the active directory when the request is unknown or its session has no directory.
    /// </summary>
    private string PermissionDirectory(string requestId)
    {
        var request = _permissions.FirstOrDefault(p => p.Id == requestId);
        if (request is not null && request.SessionId.Length > 0)
        {
            var session = GetSession(request.SessionId);
            if (session is not null && session.Directory.Length > 0) return session.Directory;
        }
        return ActiveDirectory();
    }

    /// <summary>
    /// Resolves the workspace directory that owns a pending question request, so a reply/reject
    /// reaches the instance holding it (mirrors <see cref="PermissionDirectory"/>). The mapping is
    /// seeded from question.asked events and the pending /question list; falls back to the active
    /// directory when unknown.
    /// </summary>
    private string QuestionDirectory(string requestId)
    {
        if (_questionDirectories.TryGetValue(requestId, out var directory) && directory.Length > 0)
            return directory;
        return ActiveDirectory();
    }

    /// <summary>The workspace directory of a session (or the active directory when unknown).</summary>
    private string DirectoryOf(string sessionId)
    {
        if (sessionId.Length > 0)
        {
            var session = GetSession(sessionId);
            if (session is not null && session.Directory.Length > 0) return session.Directory;
        }
        return ActiveDirectory();
    }

    /// <summary>
    /// Re-syncs pending permission requests from the server: rebuilds the per-session pending
    /// counts (drives the sidebar attention indicator) and rebuilds the active-view approval queue
    /// from the authoritative server list. The server is the source of truth — requests that were
    /// answered, or removed because their turn/instance was aborted or disposed, disappear with no
    /// <c>permission.replied</c> event, so any queue entries no longer listed must not linger as
    /// stale approval cards (replying to one would 404). Queries the active session's instance
    /// (a picked folder, or the server's default when no directory applies).
    /// </summary>
    public async Task SyncPendingPermissionsAsync()
    {
        try
        {
            var directory = ActiveDirectory();
            if (!(await _client.GetPendingPermissionsAsync(ActiveDirectory())).TryGetValue(out var requests, out var error))
            {
                ShowError(error, $"Could not sync approvals for directory {directory}");
                return;
            }

            foreach (var flags in _sessionFlags.Values) flags.PendingPermissions = 0;
            foreach (var request in requests)
            {
                if (request.Id.Length == 0) continue;
                if (request.SessionId.Length > 0) Flags(request.SessionId).PendingPermissions++;
            }

            _permissions.Clear();
            ActivePermission = null;
            foreach (var request in requests)
                AddPermissionRequest(PermissionRequestItem.From(request));
            RefreshSessionFlags();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message, "Could not sync approvals");
        }
    }

    public async Task ReplyQuestionAsync(string requestId, IReadOnlyList<IReadOnlyList<string>> answers)
    {
        var directory = QuestionDirectory(requestId);
        try
        {
            await _client.ReplyQuestionAsync(requestId, new() { Answers = answers }, directory);
            _questionDirectories.Remove(requestId);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // The request is gone server-side without a question.replied event (answered
            // elsewhere, or its turn/instance was aborted/disposed) — drop the stale entry so the
            // next pending question can surface instead of a dead 404 form.
            _questionDirectories.Remove(requestId);
            ShowWarning("The question form was dismissed — the request is no longer pending.", "Question already handled");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message, "Question reply failed");
        }
    }

    public async Task RejectQuestionAsync(string requestId)
    {
        var directory = QuestionDirectory(requestId);
        try
        {
            await _client.RejectQuestionAsync(requestId, directory);
            _questionDirectories.Remove(requestId);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _questionDirectories.Remove(requestId);
            ShowWarning("The question form was dismissed — the request is no longer pending.", "Question already handled");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message, "Question dismiss failed");
        }
    }

    /// <summary>Per-session sidebar state, keyed by session id in <see cref="_sessionFlags"/>.</summary>
    private sealed class SessionFlags
    {
        // Last-known session.status type (null until a status event/poll reports one);
        // anything other than "idle" is busy.
        public string? Status;
        // How the last finished turn ended
        public ChatOutcome Outcome;
        // Pending question.asked not yet replied/rejected.
        public int PendingQuestions;
        // Pending permission.asked not yet replied.
        public int PendingPermissions;
    }
}
