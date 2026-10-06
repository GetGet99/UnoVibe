using QuickMarkup.Infra.Collections;
using UnoVibe.Integration;
using UnoVibe.Integration.Events;
namespace UnoVibe.Providers;
[QuickRefs("""
    string? NewSessionDirectory;
    SessionId? ActiveSessionId;
    string ActiveSessionDirectory => `(ActiveSessionId is null ? NewSessionDirectory : Head(ActiveSessionId)?.Directory) ?? Connection.ServerDirectory`;
    ChatParameters ActiveChatParams => `GetActiveChatParams()`;
    SessionHead? ActiveHead => `Head(ActiveSessionId)`;
    ChatboxState ActiveChatbox => `GetActiveChatbox()`;
    """)]
partial class SessionsStateProvider
{
    OpencodeConnection Connection;
    OpencodeClient Opencode => Connection.Client;
    EventsProvider Events;
    ToastsProvider Toasts;
    NotificationProvider Notifications;
    DispatcherQueue Dispatcher;
    ModelsProvider Models;
    record Keyed<T>(string Directory, T Value);
    readonly ReactiveKeyedSet<string, Keyed<ChatParameters>> chatParamsNullSessions = new(x => x.Directory);
    readonly ReactiveKeyedSet<string, Keyed<ChatboxState>> chatBoxNullSessions = new(x => x.Directory);
    readonly ReactiveKeyedSet<SessionId, SessionHead> sessions = new(x => x.Id);
    readonly ReactiveSet<string> directoriesWithoutSession = [];
    readonly ReactiveKeyedSet<SessionId, ChatboxState> chatboxes = new(x => x.SessionId) { RerunReadFromKey = false };
    readonly ReactiveKeyedSet<string, Keyed<string?>> Branches = new(x => x.Directory);
    readonly HashSet<string> pendingDirectories = new(StringComparer.Ordinal);

    public SessionHead? Head(SessionId? sessId) => sessId is null ? null : sessions.TryGetValue(sessId, out var sessHead) ? sessHead : null;
    public string ResolveAgent(string? preferred) => preferred ?? Models.AgentOptions.FirstOrDefault() ?? "build";
    public ChatboxState? Chatbox(SessionId? sessId) => sessId is null ? null : chatboxes.TryGetValue(sessId, out var chatboxModel) ? chatboxModel : null;
    public ChatboxState EnsureChatbox(SessionId sessId)
    {
        if (Chatbox(sessId) is not {} cb)
            chatboxes.Add(cb = new(Opencode, Toasts, this, Dispatcher, sessId));
        return cb;
    }
    private ChatParameters GetActiveChatParams()
    {
        if (ActiveSessionId is not null)
        {
            return Head(ActiveSessionId)!.ChatParams;
        }
        var directory = ActiveSessionDirectory;
        if (chatParamsNullSessions.TryGetValue(directory, out var kv))
            return kv.Value;
        var chatParams = new ChatParameters();
        chatParamsNullSessions.Add(new(directory, chatParams));
        return chatParams;
    }
    private ChatboxState GetActiveChatbox()
    {
        if (ActiveSessionId is {} sessId)
        {
            if (Chatbox(sessId) is not {} cb)
                chatboxes.Add(cb = new(Opencode, Toasts, this, Dispatcher, sessId));
            return cb;
        }
        var directory = ActiveSessionDirectory;
        if (chatBoxNullSessions.TryGetValue(directory, out var kv))
            return kv.Value;
        var chatbox = new ChatboxState(Opencode, Toasts, this, Dispatcher, null);
        chatBoxNullSessions.Add(new(directory, chatbox));
        return chatbox;
    }

    public SessionsStateProvider(OpencodeConnection connection, EventsProvider events, ToastsProvider toasts, NotificationProvider notification, ModelsProvider models, DispatcherQueue dispatcher) {
        Connection = connection;
        Events = events;
        Toasts = toasts;
        Notifications = notification;
        Dispatcher = dispatcher;
        Models = models;
        RegisterEvents();
        FetchInitialSessions();
        ActiveHeadComp.Watch(x =>
        {
            x?.IsRead = true;
        });
    }
    void RegisterEvents()
    {
        Events.RegisterSessionCreated(null, UpsertSession);
        Events.RegisterSessionUpdated(null, UpsertSession);
        Events.RegisterSessionDeleted(null, DeleteSession);
        Events.RegisterSessionStatus(null, OnSessionStatus);
        Events.RegisterMessageUpdated(null, MessageUpdated);
        Events.RegisterVcsBranchUpdated(null, VcsBranchUpdated);
        Events.RegisterPermissionAsked(null, OnPermissionAsked);
        Events.RegisterPermissionReplied(null, OnPermissionReplied);
        Events.RegisterQuestionAsked(null, OnQuestionAsked);
        Events.RegisterQuestionReplied(null, OnQuestionReplied);
        Events.RegisterQuestionRejected(null, OnQuestionRejected);
    }
    void FetchInitialSessions()
        => AsyncHelper.RunAndReport(
            AddDirectoryPrivateAsync(null),
            Toasts,
            "Failed to initialize sessions",
            "Session Handler"
        );
    public Task AddDirectoryAsync(string directory) => AddDirectoryPrivateAsync(NormalizeDirectory(directory));
    static string NormalizeDirectory(string directory) => Path.TrimEndingDirectorySeparator(directory);
    private async Task AddDirectoryPrivateAsync(string? directory)
    {
        if (directory is not null)
        {
            directory = NormalizeDirectory(directory);
            if (!pendingDirectories.Add(directory))
                return;
            try
            {
                if (IsDirectoryRegistered(directory))
                    return;
                await FetchSessionsForDirectoryAsync(directory);
            }
            finally
            {
                pendingDirectories.Remove(directory);
            }
            return;
        }
        await FetchSessionsForDirectoryAsync(null);
    }
    bool IsDirectoryRegistered(string directory)
        => directoriesWithoutSession.Contains(directory) || sessions.Any(x => x.Directory == directory);
    private async Task FetchSessionsForDirectoryAsync(string? directory)
    {
        if (!(await Opencode.ListSessionsAsync(directory: directory)).TryGetValue(out var sessionsResult, out var error))
        {
            Toasts.ShowError(error, "Could not fetch initial sessions");
            return;
        }
        var directories = new HashSet<string>();
        foreach (var session in sessionsResult)
        {
            if (!sessions.ContainsKey(new(session.Id)))
            {
                directories.Add(session.Directory);
                UpsertSession(session);
            }
        }
        if (directories.Count is 0)
        {
            var finalDir = directory ?? Connection.ServerDirectory;
            if (finalDir is null)
                return;
            finalDir = NormalizeDirectory(finalDir);
            if (!sessions.Any(x => x.Directory == finalDir))
            {
                directoriesWithoutSession.Add(finalDir);
                Events.Register(finalDir);
                AsyncHelper.RunAndReport(RefreshBranchFromServerAsync(finalDir), Toasts, $"Could not fetch branch for {finalDir}", "Branch");
            }
        } else
        {
            foreach (var newDir in directories)
            {
                Events.Register(newDir);
                AsyncHelper.RunAndReport(RefreshBranchFromServerAsync(newDir), Toasts, $"Could not fetch branch for {newDir}", "Branch");
            }
        }
    }

    public void PrepareNewSession(string directory)
    {
        NewSessionDirectory = NormalizeDirectory(directory);
        ActiveSessionId = null;
    }

    public async Task<SessionHead> CreateFromPreparedSessionAsync(CancellationToken ct = default)
    {
        if (ActiveSessionId is not null) throw new InvalidOperationException("This is to be called only when session is prepared");
        var chatParams = ActiveChatParams;
        var result = await CreateAsync(NewSessionDirectory, new()
        {
            Agent = chatParams.Agent,
            Model = chatParams.Model is {} model ? new()
            {
                Id = model.Id,
                ProviderID = model.ProviderId
            } : null,
            Variant = chatParams.Variant
        }, ct);
        ActiveSessionId = result.Id;
        return result;
    }

    public async Task<SessionHead> CreateAsync(string? directory, CreateSessionRequest request, CancellationToken ct)
    {
        var sessInfo = (await Opencode.CreateSessionAsync(request, directory, ct)).GetOrThrow();
        return Register(sessInfo);
    }

    public SessionHead Register(SessionInfo newSession)
    {
        Events.Register(newSession.Directory);
        var head = UpsertSession(newSession);
        head.TouchOrder();
        return head;
    }

    void MessageUpdated(string _1, MessageUpdatedEvent e)
        => AsyncHelper.RunAndReport(async () =>
        {
            var sessId = new SessionId(e.SessionId);
            if (e.Info is not AssistantMessageInfo assistent)
                return;
            var outcome = MessageJsonHelper.ClassifyMessageOutcome(assistent);

            sessions[sessId]?.Outcome = outcome;
            if (assistent.Finish is not (null or "tool-calls") && sessions[sessId] is { IsSubagent: false })
            {
                var chatBox = Chatbox(sessId);
                if (!(chatBox is not null && await chatBox.TurnStopActionAsync(outcome, assistent.Id)))
                {
                    if (sessions.TryGetValue(sessId, out var head))
                    {
                        Dispatcher.RunOrEnqueue(() =>
                        {
                            if (ActiveSessionId != sessId)
                                head.IsRead = false;
                            head.IsBusy = false;
                        });
                        Notifications.NotifyCompleted(head, head.Outcome, sessId == ActiveSessionId);
                    }
                }
            }    
        }, Toasts, "", "Message Handling Error");

    void UpsertSession(string _1, SessionCrudEvent properties)
    {
        UpsertSession(properties.Info);
    }
    SessionHead UpsertSession(SessionInfo sessInfo)
    {
        var sessId = new SessionId(sessInfo.Id);

        directoriesWithoutSession.Remove(sessInfo.Directory);

        if (sessions.TryGetValue(sessId, out var sess))
        {
            sess.ApplyUpdateFrom(sessInfo);
        }
        else
        {
            sess = SessionHead.From(sessInfo);
            sessions.Add(sess);
        }
        return sess;
    }

    private void DeleteSession(string _1, SessionCrudEvent properties)
    {
        var sessId = new SessionId(properties.SessionId);

        if (ActiveSessionId == sessId)
            ActiveSessionId = null;
        if (!sessions.TryGetValue(sessId, out var head))
        {
            chatboxes.Remove(sessId);
            return;
        }
        foreach (var requestId in head.PendingPermissionIds.ToArray())
            ClearAncestorsPendingPermission(head, requestId);
        foreach (var requestId in head.PendingQuestionIds.ToArray())
            ClearAncestorsPendingQuestion(head, requestId);
        sessions.Remove(sessId);
        chatboxes.Remove(sessId);
    }

    void OnSessionStatus(string _, SessionStatusEvent e)
    {
        var sessId = new SessionId(e.SessionId);
        if (!sessions.TryGetValue(sessId, out var head)) return;

        head.IsBusy = e.Status is not SessionStatusIdle;
    }

    void OnPermissionAsked(string _, PermissionAskedEvent e)
    {
        if (e.Id.Length is 0 || e.SessionId.Length is 0) return;
        if (!sessions.TryGetValue(new(e.SessionId), out var head)) return;
        if (!head.PendingPermissionIds.Add(e.Id)) return;
        MarkAncestorsPendingPermission(head, e.Id);
        if (head.Id != ActiveSessionId)
            head.IsRead = false;
        var request = PermissionRequestItem.From(e);
        Notifications.NotifyPermission(head, request.Title, request.Body, IsActiveOrDescendant(head.Id));
    }

    void OnPermissionReplied(string _, PermissionRepliedEvent e)
    {
        if (e.RequestId.Length is 0 || e.SessionId.Length is 0) return;
        if (!sessions.TryGetValue(new(e.SessionId), out var head)) return;
        head.PendingPermissionIds.Remove(e.RequestId);
        ClearAncestorsPendingPermission(head, e.RequestId);
    }

    void OnQuestionAsked(string _, QuestionAskedEvent e)
    {
        if (e.Id.Length is 0 || e.SessionId.Length is 0) return;
        if (!sessions.TryGetValue(new(e.SessionId), out var head)) return;
        if (!head.PendingQuestionIds.Add(e.Id)) return;
        MarkAncestorsPendingQuestion(head, e.Id);
        if (head.Id != ActiveSessionId)
            head.IsRead = false;
        var first = e.Questions.FirstOrDefault()?.Question ?? "";
        Notifications.NotifyQuestion(head, first, head.Id == ActiveSessionId);
    }

    void OnQuestionReplied(string _, QuestionRepliedEvent e)
    {
        if (e.RequestId.Length is 0 || e.SessionId.Length is 0) return;
        if (!sessions.TryGetValue(new(e.SessionId), out var head)) return;
        head.PendingQuestionIds.Remove(e.RequestId);
        ClearAncestorsPendingQuestion(head, e.RequestId);
    }

    void OnQuestionRejected(string _, QuestionRejectedEvent e)
    {
        if (e.RequestId.Length is 0 || e.SessionId.Length is 0) return;
        if (!sessions.TryGetValue(new(e.SessionId), out var head)) return;
        head.PendingQuestionIds.Remove(e.RequestId);
        ClearAncestorsPendingQuestion(head, e.RequestId);
    }

    void MarkAncestorsPendingPermission(SessionHead head, string requestId)
    {
        var current = head.ParentId;
        var guard = 0;
        while (current is not null && guard++ < 64)
        {
            if (!sessions.TryGetValue(current, out var parent)) break;
            parent.PendingPermissionIds.Add(requestId);
            if (parent.Id != ActiveSessionId)
                parent.IsRead = false;
            current = parent.ParentId;
        }
    }

    void ClearAncestorsPendingPermission(SessionHead head, string requestId)
    {
        var current = head.ParentId;
        var guard = 0;
        while (current is not null && guard++ < 64)
        {
            if (!sessions.TryGetValue(current, out var parent)) break;
            parent.PendingPermissionIds.Remove(requestId);
            current = parent.ParentId;
        }
    }

    void MarkAncestorsPendingQuestion(SessionHead head, string requestId)
    {
        var current = head.ParentId;
        var guard = 0;
        while (current is not null && guard++ < 64)
        {
            if (!sessions.TryGetValue(current, out var parent)) break;
            parent.PendingQuestionIds.Add(requestId);
            if (parent.Id != ActiveSessionId)
                parent.IsRead = false;
            current = parent.ParentId;
        }
    }

    void ClearAncestorsPendingQuestion(SessionHead head, string requestId)
    {
        var current = head.ParentId;
        var guard = 0;
        while (current is not null && guard++ < 64)
        {
            if (!sessions.TryGetValue(current, out var parent)) break;
            parent.PendingQuestionIds.Remove(requestId);
            current = parent.ParentId;
        }
    }

    bool IsActiveOrDescendant(SessionId sessionId)
    {
        if (ActiveSessionId is null) return false;
        SessionId? current = sessionId;
        var guard = 0;
        while (current is not null && guard++ < 64)
        {
            if (current == ActiveSessionId) return true;
            current = Head(current)?.ParentId;
        }
        return false;
    }

    void VcsBranchUpdated(string directory, VcsBranchUpdatedEvent e)
        => AsyncHelper.RunAndReport(
            RefreshBranchAsync(directory, e.Branch),
            Toasts,
            "Failed to update branch",
            "Branch Update"
        );

    async Task RefreshBranchAsync(string directory, string? branch)
    {
        Branches.AddOrReplace(new(directory, branch));
    }

    async Task RefreshBranchFromServerAsync(string directory)
    {
        if (!(await Opencode.GetVCSInfoAsync(directory)).TryGetValue(out var vcs, out _))
            return;
        Branches.AddOrReplace(new(directory, vcs.Branch));
    }

    string? GetBranch(string directory)
        => Branches.TryGetValue(directory, out var db) ? db.Value : null;

    public IEnumerable<SessionGroupModel> SessionSidebar =>
        directoriesWithoutSession.Select(x => new SessionGroupModel(x, GetBranch(x), [])).Concat(
            sessions
            .Where(s => !s.IsSubagent)
            .OrderByDescending(s => s.SortKey).ThenByDescending(s => s.Id.Id, StringComparer.Ordinal)
            .GroupBy(s => s.Directory)
            .Select(g => new SessionGroupModel(g.Key, GetBranch(g.Key), [.. g]))
        );

    public IEnumerable<SessionHead> SubagentsHeadOf(SessionId? sessId) =>
        sessId is null ? [] :
        sessions
        .Where(s => s.ParentId == sessId)
        .OrderByDescending(s => s.Created).ThenByDescending(s => s.Id.Id, StringComparer.Ordinal);
}
record SessionGroupModel(string Directory, string? Branch, List<SessionHead> Sessions);