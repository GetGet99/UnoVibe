using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using UnoVibe.Models;
using static UnoVibe.Integration.ResultExtension;
using OpencodeClient = UnoVibe.Integration.OpencodeClient;
using OpencodeEvent = UnoVibe.Integration.OpencodeEvent;
namespace UnoVibe.Services;

[QuickMarkup("""
    public string DisplayLabel = "";
    public string ServerDirectory = "";
    public string ConnectionUrl = "";
    public string ConnectionPassword = "";
    public int SubagentCount;
    public PermissionRequestItem? ActivePermission;
    public ToastItem? CurrentToast;
    public SessionStore Active = `NewDraftStore()`;
    """)]
[Obsolete("This class will be removed", error: true)]
public sealed partial class ChatStoreToBeRemoved : IDisposable
{
    public ObservableCollection<string> ModeOptions { get; } = new();
    public ObservableCollection<ModelOption> ModelOptions { get; } = new();
    public ObservableCollection<string> VariantOptions { get; } = new();

    public ObservableCollection<SessionInfoToRemove> SessionsToRemove { get; } = new();
    public ObservableCollection<SessionInfoToRemove> ActiveSubagents { get; } = new();

    public Window? OwnerWindow { get; set; }

    public OpencodeClient? Client => _client;

    private OpencodeClient _client = null!;
    private readonly Channel<OpencodeEvent> _events = Channel.CreateUnbounded<OpencodeEvent>();
    private readonly Dictionary<string, SessionFlags> _sessionFlags = new();
    private readonly Dictionary<string, DirectoryGroup> _groupsByDirectory = new();
    private readonly Dictionary<string, SessionInfoToRemove> _sessionsById = new();
    private readonly List<PermissionRequestItem> _permissions = new();
    private readonly Dictionary<string, string> _questionDirectories = new();
    private CancellationTokenSource? _cts;
    private DispatcherQueue? _dispatcher;
    private bool _started;
    private string? _pendingDirectory;
    private readonly Dictionary<string, CancellationTokenSource> _folderStreamCts = new();
    private const int MaxSeenEventIds = 2000;
    private readonly HashSet<string> _seenEventIds = new();
    private readonly Queue<string> _seenEventIdOrder = new();

    private bool _creatingSession;
    private bool _refreshingSessions;
    private bool _refreshSessionsQueued;

    private readonly Dictionary<string, SessionStoreToBeRemoved> _sessionStores = new();

    public event Action? ActiveStoreChanged;

    private SessionStoreToBeRemoved NewDraftStore()
    {
        var store = new SessionStoreToBeRemoved();
        store.Router = this;
        store.SessionId = "";
        return store;
    }

    private SessionStoreToBeRemoved NewCachedStore(string sessionId)
    {
        var store = new SessionStoreToBeRemoved();
        store.Router = this;
        store.SessionId = sessionId;
        _sessionStores[sessionId] = store;
        return store;
    }

    private SessionStoreToBeRemoved? GetStore(string sessionId) =>
        sessionId.Length == 0 ? null : _sessionStores.GetValueOrDefault(sessionId);

    public SessionInfoToRemove? GetSession(string sessionId) =>
        sessionId.Length == 0 ? null : _sessionsById.GetValueOrDefault(sessionId);

    private SessionFlags Flags(string sessionId) =>
        _sessionFlags.TryGetValue(sessionId, out var flags)
            ? flags
            : _sessionFlags[sessionId] = new SessionFlags();

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

    public async Task<bool> EnsureSessionAsync()
    {
        if (Active.SessionId.Length > 0) return true;
        while (_creatingSession) await Task.Delay(10);
        if (Active.SessionId.Length > 0) return true;

        _creatingSession = true;
        try
        {
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

    public async Task RefreshSessionStatusAsync(CancellationToken ct = default)
    {
        foreach (var flags in _sessionFlags.Values) flags.Status = null;
        if (!(await _client.GetSessionStatusAsync(ct)).TryGetValue(out var statuses, out var error))
        {
            ShowError(error, "Could not refresh session status");
            return;
        }
        foreach (var kv in statuses) Flags(kv.Key).Status = kv.Value.Type;
        foreach (var s in SessionsToRemove) ApplySessionFlags(s);
    }

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

    public bool IsSessionBusy(string sessionId)
    {
        if (GetStore(sessionId) is { } store && store.Head.IsBusy) return true;
        return _sessionFlags.GetValueOrDefault(sessionId)?.Status is not (null or "idle");
    }

    private void ApplySessionFlags(SessionInfoToRemove session)
    {
        var flags = _sessionFlags.GetValueOrDefault(session.Id);
        session.Head.IsBusy = flags?.Status is not (null or "idle");
        session.Head.Outcome = flags?.Outcome ?? ChatOutcome.None;
        session.Head.IsPendingQuestion = (flags?.PendingQuestions ?? 0) > 0;
        session.Head.IsPendingPermission = (flags?.PendingPermissions ?? 0) > 0;
    }

    private void RefreshSessionFlags()
    {
        foreach (var s in SessionsToRemove) ApplySessionFlags(s);
    }

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

    private void RegisterOpenedFolder(string directory)
    {
        var path = directory.Trim();
        while (path.Length > 1 && path.EndsWith('/')) path = path[..^1];
        if (path.Length == 0) return;
        _openedFolders[path] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        StartFolderEventStream(path);
    }

    private void StartFolderEventStream(string directory)
    {
        if (_client is null || _folderStreamCts.ContainsKey(directory)) return;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts?.Token ?? CancellationToken.None);
        _folderStreamCts[directory] = cts;
        _ = Task.Run(() => _client.ReadEventAsync(_events.Writer, cts.Token, directory));
    }

    public void ToggleDirectoryExpanded(string directory)
    {
        if (_groupsByDirectory.TryGetValue(directory, out var group))
            group.IsExpanded = !group.IsExpanded;
    }

    private void ReconcileActiveSubagents()
    {
        var sessionId = Active?.Head.Id;
        var desired = sessionId is null
            ? new List<SessionInfoToRemove>()
            : SessionsToRemove.Where(s => s.Head.ParentId == sessionId).OrderByDescending(s => s.Head.Updated).ToList();
        ReconcileSessionCollection(ActiveSubagents, desired);
        SubagentCount = ActiveSubagents.Count;
    }

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

    private void ApplySessionUpsert(JsonElement properties)
    {
        GetStore(session.Id)?.ApplySessionInfo(SessionInfoToRemove.From(session), info);
    }

    private static Integration.SessionInfo SessionInfoFromJson(JsonElement item)
        => item.Deserialize(AppJsonContext.Default.SessionInfo)!;

    public async Task SwitchSessionAsync(string sessionId)
    {
        if (sessionId.Length == 0 || sessionId == Active.SessionId) return;

        var known = GetSession(sessionId);
        var cached = GetStore(sessionId);
        var store = cached ?? NewCachedStore(sessionId);

        Active = store;
        ActiveSessionId = sessionId;
        ActiveStoreChanged?.Invoke();

        if (store.SessionId.Length > 0) store.IsBusy = IsSessionBusy(sessionId);

        known?.Head.IsRead = true;

        ReconcileActiveSubagents();

        if (cached is not null)
        {
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
                ApplySessionStatus(evt.Properties);
                break;

            case "question.asked":
                ApplyQuestionAsked(evt.Properties);
                break;
            case "question.replied":
            case "question.rejected":
                ApplyQuestionReplied(evt.Properties);
                break;

            case "permission.asked":
                ApplyPermissionAsked(evt.Properties);
                break;
            case "permission.replied":
                ApplyPermissionReplied(evt.Properties);
                break;

            case "session.created":
            case "session.updated":
                ApplySessionUpsert(evt.Properties);
                break;
            case "session.deleted":
                ApplySessionDeleted(evt.Properties);
                break;

            case "vcs.branch.updated":
                RefreshBranches();
                break;
        }
    }

    private void DispatchToSession(JsonElement properties, Action<SessionStoreToBeRemoved, JsonElement> apply)
    {
        var sessionId = properties.GetStringProperty("sessionID");
        if (sessionId.Length == 0) return;
        if (_sessionStores.TryGetValue(sessionId, out var store)) apply(store, properties);
    }

    private void ApplySessionStatus(JsonElement properties)
    {
        if (!properties.TryGetProperty("status", out var status)) return;
        var type = status.GetStringProperty("type");

        var sessionId = properties.GetStringProperty("sessionID");
        var store = sessionId.Length > 0 ? GetStore(sessionId) : null;

        store?.ApplySessionStatus(properties);
    }

    private void ApplyQuestionAsked(JsonElement properties)
    {
        var requestId = properties.GetStringProperty("id");
        if (requestId.Length == 0) return;

        var sessionId = properties.GetStringProperty("sessionID");
        if (sessionId.Length > 0)
        {
            Flags(sessionId).PendingQuestions++;
            var item = GetSession(sessionId);
            if (item is not null) ApplySessionFlags(item);
            if (item is not null)
                NotificationsHelper.NotifyQuestion(OwnerWindow, item, FirstQuestionText(properties),
                    sessionId == Active.SessionId);
            _questionDirectories[requestId] = DirectoryOf(sessionId);
        }

        GetStore(sessionId)?.ApplyQuestionAsked(properties);
    }

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

        var sessionId = properties.GetStringProperty("sessionID");
        if (sessionId.Length > 0)
        {
            Flags(sessionId).PendingPermissions++;
            var item = GetSession(sessionId);
            if (item is not null) ApplySessionFlags(item);
        }

        var request = PermissionRequestItem.FromJson(properties);
        if (!_permissions.Any(p => p.Id == request.Id) && sessionId.Length > 0 && GetSession(sessionId) is { } pending)
            NotificationsHelper.NotifyPermission(OwnerWindow, pending, request.Title, request.Body,
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
            RemovePermissionRequest(requestId);
            ShowWarning("The approval card was dismissed — the request is no longer pending.", "Permission already handled");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message, "Approval reply failed");
        }
    }

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

    private string QuestionDirectory(string requestId)
    {
        if (_questionDirectories.TryGetValue(requestId, out var directory) && directory.Length > 0)
            return directory;
        return ActiveDirectory();
    }

    private string DirectoryOf(string sessionId)
    {
        if (sessionId.Length > 0)
        {
            var session = GetSession(sessionId);
            if (session is not null && session.Directory.Length > 0) return session.Directory;
        }
        return ActiveDirectory();
    }

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

    private sealed class SessionFlags
    {
        public string? Status;
        public ChatOutcome Outcome;
        public int PendingQuestions;
        public int PendingPermissions;
    }
}
