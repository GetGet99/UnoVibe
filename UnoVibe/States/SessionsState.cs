using System.Diagnostics.CodeAnalysis;
using QuickMarkup.Infra.Collections;
using UnoVibe.Integration;
using UnoVibe.Integration.Events;
using UnoVibe.Models;
namespace UnoVibe.States;
[QuickMarkup("""
    using UnoVibe.Models;
    string? NewSessionDirectory;
    SessionId? ActiveSessionId;
    string ActiveSessionDirectory => `(Sessions.ActiveSessionId is null ? Sessions.NewSessionDirectory : Sessions.Head(Sessions.ActiveSessionId)?.Directory) ?? connection.ServerDirectory`;
    ChatParameters ActiveChatParams => `GetActiveChatParams()`;
    SessionHead? ActiveHead => `Head(ActiveSessionId)`;
    ChatboxState ActiveChatbox => `GetActiveChatbox()`;
    """)]
partial class SessionsState
{
    OpencodeConnection Connection;
    OpencodeClient Opencode => Connection.Client;
    EventsProvider Events;
    ToastsProvider Toasts;
    NotificationService Notifications;
    DispatcherQueue Dispatcher;
    record Keyed<T>(string Directory, T Value);
    readonly ReactiveKeyedSet<string, Keyed<ChatParameters>> chatParamsNullSessions = new(x => x.Directory);
    readonly ReactiveKeyedSet<string, Keyed<ChatboxState>> chatBoxNullSessions = new(x => x.Directory);
    readonly ReactiveKeyedSet<SessionId, SessionHead> sessions = new(x => x.Id);
    readonly ReactiveSet<string> directoriesWithoutSession = [];
    readonly ReactiveKeyedSet<SessionId, ChatboxState> chatboxes = new(x => x.SessionId) { RerunReadFromKey = false };

    public SessionHead? Head(SessionId? sessId) => sessId is null ? null : sessions.TryGetValue(sessId, out var sessHead) ? sessHead : null;
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

    [QuickMarkupConstructor]
    [MemberNotNull(nameof(Connection), nameof(Events), nameof(Toasts), nameof(Notifications), nameof(Dispatcher))]
    void Ctor(OpencodeConnection connection, EventsProvider events, ToastsProvider toasts, NotificationService notification, DispatcherQueue dispatcher) {
        Connection = connection;
        Events = events;
        Toasts = toasts;
        Notifications = notification;
        Dispatcher = dispatcher;
        Init(connection, events, toasts, notification, dispatcher);
        RegisterEvents();
        FetchInitialSessions();
    }
    void RegisterEvents()
    {
        Events.RegisterSessionCreated(null, UpsertSession);
        Events.RegisterSessionUpdated(null, UpsertSession);
        Events.RegisterSessionDeleted(null, DeleteSession);
        Events.RegisterMessageUpdated(null, MessageUpdated);
    }
    private async void FetchInitialSessions() => await AddDirectoryPrivateAsync(null);
    public Task AddDirectoryAsync(string directory) => AddDirectoryPrivateAsync(directory);
    private async Task AddDirectoryPrivateAsync(string? directory)
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
                sessions.Add(SessionHead.From(session));
            }
        }
        if (directories.Count is 0)
        {
            // add to directory without session
            var finalDir = directory ?? Connection.ServerDirectory;
            if (!sessions.Any(x => x.Directory == finalDir))
                directoriesWithoutSession.Add(finalDir);
        } else
        {
            foreach (var newDir in directories)
                Events.Register(newDir);
        }
    }

    public void PrepareNewSession(string directory)
    {
        NewSessionDirectory = directory;
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
        return UpsertSession(newSession);
    }

    async void MessageUpdated(string _1, MessageUpdatedEvent e)
    {
        var sessId = new SessionId(e.SessionId);
        // Feed the sidebar outcome tracker for assistant message completions. The last
        // update for a turn carries its definitive outcome (error/finish/cost/tokens).
        if (e.Info is not AssistantMessageInfo assistent)
            return;
        var outcome = MessageJsonHelper.ClassifyMessageOutcome(assistent);
        
        sessions[sessId]?.Outcome = outcome;
        if (assistent.Finish is not null)
        {
            var meetsContinueCriteria = outcome is not ChatOutcome.Interrupted;
            var meetsAutoContinueCriteria = meetsContinueCriteria;

            if (meetsContinueCriteria)
            {
                try
                {
                    var message = (await Opencode.GetMessageAsync(sessId, assistent.Id)).GetOrThrow();
                    if (message.Parts is { Count: > 0 } parts)
                    {
                        var lastPart = parts[^1];
                        // Ended on thinking — auto-continue to get the actual response.
                        meetsContinueCriteria = lastPart is ReasoningPart;
                        meetsAutoContinueCriteria = meetsContinueCriteria && SettingsStore.AutoContinueOnThinking;
                    }
                }
                catch
                {
                    // Best-effort: if the fetch fails, fall back to no (auto-)continue.
                    meetsContinueCriteria = false;
                    meetsAutoContinueCriteria = false;
                }
            }

            if (!(Chatbox(sessId)?.TurnStopAction(meetsContinueCriteria, meetsAutoContinueCriteria) ?? false))
            {
                if (sessions.TryGetValue(sessId, out var head))
                {
                    if (ActiveSessionId != sessId)
                        head.IsRead = false;
                    head.IsBusy = false;
                    Notifications.NotifyCompleted(head, head.Outcome, sessId == ActiveSessionId);
                }
            }
        }
    }
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

    /// <summary>
    /// Applies a <c>session.deleted</c> event: removes the session from the sidebar and the
    /// store cache immediately, and clears the active view if the deleted session was active.
    /// </summary>
    private void DeleteSession(string _1, SessionCrudEvent properties)
    {
        var sessId = new SessionId(properties.SessionId);

        if (ActiveSessionId == sessId)
            ActiveSessionId = null;
        sessions.Remove(sessId);
        chatboxes.Remove(sessId);
    }

    public IEnumerable<(string Directory, string? Branch, List<SessionHead> Sessions)> SessionSidebar =>
        directoriesWithoutSession.Select(x => (x, (string?)"Branch", (List<SessionHead>)[])).Concat(
            sessions
            .Where(s => !s.IsSubagent)
            .OrderByDescending(s => s.Updated).ThenByDescending(s => s.Id)
            .GroupBy(s => s.Directory)
            .Select(g => (g.Key, (string?)"Branch", g.ToList()))
        );
}