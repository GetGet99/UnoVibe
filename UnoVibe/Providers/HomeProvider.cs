using QuickMarkup.Infra.Collections;
using UnoVibe.Integration;
using UnoVibe.Integration.Events;

namespace UnoVibe.Providers;

[QuickRefs("""
    bool IsHomeView = true;
    bool IsCreatingSession = false;
    string? DraftDirectory = null;
    `IEnumerable<string>` DraftDirectoryOptions => `OrderedDirectories()`;
    """)]
partial class HomeProvider
{
    const int MaxPins = 20;

    readonly ReactiveKeyedSet<SessionId, HomeCardSnapshot> cards = new(x => x.SessionId);
    readonly HashSet<SessionId> refreshing = new();

    OpencodeClient Opencode;
    EventsProvider Events;
    ToastsProvider Toasts;
    SessionsStateProvider Sessions;
    ModelsProvider Models;
    DispatcherQueue Dispatcher;

    public HomeProvider(OpencodeClient opencode, EventsProvider events, ToastsProvider toasts, SessionsStateProvider sessions, ModelsProvider models, DispatcherQueue dispatcher)
    {
        Opencode = opencode;
        Events = events;
        Toasts = toasts;
        Sessions = sessions;
        Models = models;
        Dispatcher = dispatcher;
        RegisterEvents();
    }

    public bool IsPinned(SessionId id) => cards.ContainsKey(id);

    public HomeCardSnapshot? Card(SessionId id) => cards.TryGetValue(id, out var card) ? card : null;

    public int PinnedCount => cards.Count;

    public IEnumerable<HomeCardSnapshot> CardsOrdered =>
        cards
        .OrderByDescending(c => Sessions.Head(c.SessionId)?.SortKey ?? 0)
        .ThenByDescending(c => c.SessionId.Id, StringComparer.Ordinal);

    public void Pin(SessionId id)
    {
        if (cards.ContainsKey(id)) return;
        if (Sessions.Head(id) is null) return;
        var oldest = CardsOrdered.LastOrDefault();
        while (cards.Count >= MaxPins && oldest is not null)
        {
            cards.Remove(oldest.SessionId);
            oldest = CardsOrdered.LastOrDefault();
        }
        cards.Add(new HomeCardSnapshot(id));
        AsyncHelper.RunAndReport(RefreshPreviewAsync(id), Toasts, "Could not load preview", "Home");
    }

    public void Unpin(SessionId id)
    {
        if (Sessions.Head(id) is {} head)
            head.IsRead = true;
        cards.Remove(id);
    }

    public void TogglePin(SessionId id)
    {
        if (IsPinned(id)) Unpin(id);
        else Pin(id);
    }

    public void OpenSession(SessionId id)
    {
        IsHomeView = false;
        if (Sessions.Head(id) is {} head)
            head.IsRead = true;
        Sessions.ActiveSessionId = id;
    }

    public void StartCreating()
    {
        if (IsCreatingSession) return;
        IsCreatingSession = true;
        SetDraftDirectory(DraftDirectoryOptions.FirstOrDefault() ?? Sessions.ActiveSessionDirectory);
    }

    public void SetDraftDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        DraftDirectory = Path.TrimEndingDirectorySeparator(directory);
        Sessions.PrepareNewSession(DraftDirectory);
    }

    public void CancelCreating()
    {
        IsCreatingSession = false;
        DraftDirectory = null;
        if (Sessions.ActiveSessionId is null)
            Sessions.ActiveChatbox.Message = new();
    }

    public void CompleteDraft(SessionId id)
    {
        IsCreatingSession = false;
        DraftDirectory = null;
        Pin(id);
    }

    public async Task<ChatboxState> EnsureDraftChatboxAsync()
    {
        var chatbox = Sessions.ActiveChatbox;
        if (chatbox.SessionId is not null) return chatbox;
        await Sessions.CreateFromPreparedSessionAsync();
        var resolved = Sessions.ActiveChatbox;
        if (!ReferenceEquals(chatbox, resolved))
        {
            resolved.Message = chatbox.Message;
            chatbox.Message = new();
        }
        return resolved;
    }

    IEnumerable<string> OrderedDirectories()
    {
        var pinnedDirs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in CardsOrdered)
        {
            if (Sessions.Head(card.SessionId)?.Directory is {} dir)
                pinnedDirs.Add(dir);
        }
        return Sessions.SessionSidebar
            .Select((group, index) => (group.Directory, index))
            .OrderBy(x => pinnedDirs.Contains(x.Directory))
            .ThenBy(x => x.index)
            .Select(x => x.Directory)
            .Distinct(StringComparer.Ordinal);
    }

    public void GoHome()
    {
        Sessions.ActiveSessionId = null;
        IsHomeView = true;
    }

    public async Task RefreshPreviewAsync(SessionId id)
    {
        if (!cards.ContainsKey(id)) return;
        if (!refreshing.Add(id)) return;
        try
        {
            var messagesResult = await Opencode.GetMessagesAsync(id.Id);
            var directory = Sessions.Head(id)?.Directory;
            List<PendingQuestion>? questions = null;
            List<PermissionRequestDto>? permissions = null;
            if (directory is not null)
            {
                if ((await Opencode.GetPendingQuestionsAsync(directory)).TryGetValue(out var q, out _))
                    questions = q;
                if ((await Opencode.GetPendingPermissionsAsync(directory)).TryGetValue(out var p, out _))
                    permissions = p;
            }
            var ok = messagesResult.TryGetValue(out var messages, out _);
            Dispatcher.TryEnqueue(() =>
            {
                if (!cards.TryGetValue(id, out var card)) return;
                ApplyPreview(card, ok ? messages : null, questions, permissions);
            });
        }
        finally
        {
            refreshing.Remove(id);
        }
    }

    void ApplyPreview(HomeCardSnapshot card, List<MessageWithParts>? messages, List<PendingQuestion>? questions, List<PermissionRequestDto>? permissions)
    {
        card.IsLoading = false;
        if (messages is not null)
        {
            var parsed = MessageCollectionHelper.ParseMessages(messages);
            if (questions is not null)
                MessageCollectionHelper.AttachPendingQuestions(
                    id => parsed.FirstOrDefault(m => m.Id == id), card.SessionId.Id, questions);
            var (cost, tokens, last) = SessionUsageHelper.Summarize(parsed);
            card.Cost = cost;
            card.Tokens = tokens;
            card.ContextLimit = SessionUsageHelper.ResolveContextLimit(Models.ModelOptions, last);
            RefillPreview(card, parsed);
        }
        if (permissions is not null)
        {
            var pending = permissions.FirstOrDefault(p => p.SessionId == card.SessionId.Id);
            card.ActivePermission = pending is not null ? PermissionRequestItem.From(pending) : null;
        }
        if (questions is not null)
        {
            MessageCollectionHelper.AttachPendingQuestions(
                id => card.PreviewMessages.FirstOrDefault(m => m.Id == id), card.SessionId.Id, questions);
        }
    }

    static void RefillPreview(HomeCardSnapshot card, List<MessageItem> parsed)
    {
        card.PreviewMessages.Clear();
        foreach (var message in SelectPreviewMessages(parsed))
            card.PreviewMessages.Add(message);
        EnforceSingleTool(card);
        foreach (var message in card.PreviewMessages)
            MessageJsonHelper.LoadPartImages(message);
    }

    static List<MessageItem> SelectPreviewMessages(List<MessageItem> parsed)
    {
        MessageItem? lastUser = null;
        MessageItem? lastText = null;
        MessageItem? lastTool = null;
        foreach (var message in parsed)
        {
            if (message.Role == "user")
            {
                lastUser = message;
            }
            else if (message.Role == "assistant")
            {
                if (message.Parts.OfType<TextPartItem>().Any(t => !t.Synthetic && t.Text.Trim().Length > 0))
                    lastText = message;
                if (message.Parts.OfType<ToolCallPartItem>().Any())
                    lastTool = message;
            }
        }
        var selected = new List<MessageItem>();
        foreach (var message in new[] { lastUser, lastText, lastTool })
        {
            if (message is not null && !selected.Any(s => s.Id == message.Id))
                selected.Add(message);
        }
        selected.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
        return selected;
    }

    static void EnforceSingleTool(HomeCardSnapshot card)
    {
        string? lastToolMessageId = null;
        string? lastToolPartId = null;
        foreach (var message in card.PreviewMessages)
        {
            foreach (var part in message.Parts)
            {
                if (part is ToolCallPartItem)
                {
                    lastToolMessageId = message.Id;
                    lastToolPartId = part.Id;
                }
            }
        }
        var filter = new Dictionary<string, HashSet<string>>();
        foreach (var message in card.PreviewMessages)
        {
            if (message.Role != "assistant") continue;
            var visible = new HashSet<string>();
            foreach (var part in message.Parts)
            {
                if (part is ToolCallPartItem)
                {
                    if (message.Id == lastToolMessageId && part.Id == lastToolPartId)
                        visible.Add(part.Id);
                }
                else visible.Add(part.Id);
            }
            filter[message.Id] = visible;
        }
        card.PartFilter = filter;
    }

    public async Task StopCardAsync(HomeCardSnapshot card)
    {
        try
        {
            await Opencode.AbortAsync(card.SessionId.Id);
        }
        catch (Exception ex)
        {
            Toasts.ShowError(ex.Message, "Stop failed");
        }
    }

    public async Task ReplyPermissionAsync(SessionId sessionId, string requestId, string reply, string? message = null)
    {
        if (!cards.TryGetValue(sessionId, out var card)) return;
        var directory = Sessions.Head(sessionId)?.Directory;
        if (await PendingRequestHelper.ReplyPermissionAsync(Opencode, Toasts, directory, requestId, reply, message)
            && card.ActivePermission?.Id == requestId)
            card.ActivePermission = null;
    }

    public async Task ReplyQuestionAsync(SessionId sessionId, string requestId, IReadOnlyList<IReadOnlyList<string>> answers)
    {
        var directory = Sessions.Head(sessionId)?.Directory;
        await PendingRequestHelper.ReplyQuestionAsync(Opencode, Toasts, directory, requestId, answers);
    }

    public async Task RejectQuestionAsync(SessionId sessionId, string requestId)
    {
        var directory = Sessions.Head(sessionId)?.Directory;
        await PendingRequestHelper.RejectQuestionAsync(Opencode, Toasts, directory, requestId);
    }

    void RegisterEvents()
    {
        Events.RegisterMessageUpdated(null, OnMessageUpdated);
        Events.RegisterMessagePartUpdated(null, OnPartUpdated);
        Events.RegisterMessagePartDelta(null, OnPartDelta);
        Events.RegisterMessagePartRemoved(null, OnPartRemoved);
        Events.RegisterMessageRemoved(null, OnMessageRemoved);
        Events.RegisterSessionUpdated(null, OnSessionUpdated);
        Events.RegisterSessionStatus(null, OnSessionStatus);
        Events.RegisterSessionDeleted(null, OnSessionDeleted);
        Events.RegisterQuestionAsked(null, OnQuestionAsked);
        Events.RegisterQuestionReplied(null, OnQuestionReplied);
        Events.RegisterQuestionRejected(null, OnQuestionRejected);
        Events.RegisterPermissionAsked(null, OnPermissionAsked);
        Events.RegisterPermissionReplied(null, OnPermissionReplied);
        Events.RegisterTodoUpdated(null, OnTodoUpdated);
    }

    void RefreshCard(string sessionId)
    {
        var id = new SessionId(sessionId);
        if (!cards.ContainsKey(id)) return;
        AsyncHelper.RunAndReport(RefreshPreviewAsync(id), Toasts, "", "Home preview");
    }

    void OnMessageUpdated(string _, MessageUpdatedEvent e) => RefreshCard(e.SessionId);

    void OnSessionUpdated(string _, SessionCrudEvent e) => RefreshCard(e.SessionId);

    void OnSessionDeleted(string _, SessionCrudEvent e) => cards.Remove(new(e.SessionId));

    void OnSessionStatus(string _, SessionStatusEvent e)
    {
        if (e.Status is not SessionStatusIdle) return;
        RefreshCard(e.SessionId);
    }

    void OnPartUpdated(string _, MessagePartUpdatedEvent e)
    {
        if (!cards.TryGetValue(new(e.SessionId), out var card)) return;
        var message = card.PreviewMessages.FirstOrDefault(m => m.Id == e.Part.MessageId);
        if (message is null)
        {
            RefreshCard(e.SessionId);
            return;
        }
        var outcome = MessageCollectionHelper.UpsertPart(message, e.Part, out var affected);
        if (outcome == PartUpsert.SyntheticEmpty)
        {
            card.PreviewMessages.Remove(message);
            return;
        }
        if (outcome == PartUpsert.Added)
            EnforceSingleTool(card);
        if (affected is FilePartItem file)
            AsyncHelper.RunAndReport(file.LoadImageAsync(), Toasts, "", "Load image");
    }

    void OnPartDelta(string _, MessagePartDeltaEvent e)
    {
        if (!cards.TryGetValue(new(e.SessionId), out var card)) return;
        MessageCollectionHelper.AppendDelta(card.PreviewMessages.FirstOrDefault(m => m.Id == e.MessageId), e);
    }

    void OnPartRemoved(string _, MessagePartRemovedEvent e)
    {
        if (!cards.TryGetValue(new(e.SessionId), out var card)) return;
        var message = card.PreviewMessages.FirstOrDefault(m => m.Id == e.MessageId);
        if (message is null) return;
        MessageCollectionHelper.RemovePart(message, e.PartId);
        EnforceSingleTool(card);
    }

    void OnMessageRemoved(string _, MessageRemovedEvent e)
    {
        if (e.MessageId.Length == 0) return;
        if (!cards.TryGetValue(new(e.SessionId), out var card)) return;
        var message = card.PreviewMessages.FirstOrDefault(m => m.Id == e.MessageId);
        if (message is null) return;
        card.PreviewMessages.Remove(message);
        RefreshCard(e.SessionId);
    }

    void OnQuestionAsked(string _, QuestionAskedEvent e)
    {
        if (!cards.TryGetValue(new(e.SessionId), out var card)) return;
        if (e.Tool is null) return;
        var message = card.PreviewMessages.FirstOrDefault(m => m.Id == e.Tool.MessageId);
        if (!MessageCollectionHelper.AttachLiveQuestion(message, e.Tool.CallId, e.Id, e.Questions))
        {
            RefreshCard(e.SessionId);
            return;
        }
        EnforceSingleTool(card);
    }

    void OnQuestionReplied(string _, QuestionRepliedEvent e) => ClearQuestion(e.SessionId, e.RequestId);

    void OnQuestionRejected(string _, QuestionRejectedEvent e) => ClearQuestion(e.SessionId, e.RequestId);

    void ClearQuestion(string sessionId, string requestId)
    {
        if (!cards.TryGetValue(new(sessionId), out var card)) return;
        MessageCollectionHelper.ClearQuestion(card.PreviewMessages, requestId);
    }

    void OnPermissionAsked(string _, PermissionAskedEvent e)
    {
        if (e.Id.Length is 0 || e.SessionId.Length is 0) return;
        if (!cards.TryGetValue(new(e.SessionId), out var card)) return;
        card.ActivePermission = PermissionRequestItem.From(e);
    }

    void OnPermissionReplied(string _, PermissionRepliedEvent e)
    {
        if (!cards.TryGetValue(new(e.SessionId), out var card)) return;
        if (card.ActivePermission?.Id != e.RequestId) return;
        card.ActivePermission = null;
    }

    void OnTodoUpdated(string _, TodoUpdatedEvent e)
    {
        if (!cards.TryGetValue(new(e.SessionId), out var card)) return;
        card.TodoDone = e.Todos.Count(t => t.Status == "completed");
        card.TodoTotal = e.Todos.Count;
    }
}
