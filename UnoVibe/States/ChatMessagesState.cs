using UnoVibe.Helpers;
using UnoVibe.Integration;
using UnoVibe.Integration.Events;
using UnoVibe.Models;
using UnoVibe.Providers;

namespace UnoVibe.States;

/// <summary>
/// Session-scoped reactive state owning the message list, cost/tokens, revert marker,
/// retry state, permission queue, and question handlers for a single chat session.
/// Created by <see cref="Pages.Chat.ChatPage"/> when the active session changes and
/// disposed when switching away.
///
/// Chat UI components read from this state. Formatting is the caller's responsibility —
/// this state stores raw values.
/// </summary>
[QuickMarkup("""
    double Cost;
    SessionTokens Tokens = `SessionTokens.Zero`;
    long ContextLimit;
    int TruncatedMessagesCount;
    string RevertMessageId = "";
    int RevertCount;
    RetryState Retry = `RetryState.None`;
    PermissionRequestItem? ActivePermission;
    """)]
partial class ChatMessagesState : IDisposable
{
    /// <summary>Maximum number of messages kept in the UI; older ones are dropped for rendering performance.</summary>
    public const int MaxVisibleMessages = 200;

    public SessionId SessionId { get; private set; }
    public ObservableCollection<MessageItem> Messages { get; } = [];
    readonly Dictionary<string, MessageItem> _messagesById = new();

    readonly List<PermissionRequestItem> _permissions = [];

    OpencodeClient Opencode;
    ToastsProvider Toasts;
    EventsProvider Events;
    ModelsProvider Models;
    SessionsStateProvider Sessions;

    [QuickMarkupConstructor]
    void Ctor(OpencodeClient opencode, ToastsProvider toasts,
              EventsProvider events, ModelsProvider models,
              SessionsStateProvider sessions, SessionId sessionId)
    {
        Opencode = opencode;
        Toasts = toasts;
        Events = events;
        Models = models;
        Sessions = sessions;
        SessionId = sessionId;
        RegisterEvents();
    }

    public static async Task<ChatMessagesState> Create(
        OpencodeClient opencode, ToastsProvider toasts,
        EventsProvider events, ModelsProvider models,
        SessionsStateProvider sessions, SessionId sessionId)
    {
        var state = new ChatMessagesState(opencode, toasts, events, models, sessions, sessionId);
        await state.FetchInitialStateAsync();
        return state;
    }

    // ── Initial fetch ───────────────────────────────────────────────────────

    async Task FetchInitialStateAsync()
    {
        var head = Sessions.Head(SessionId);
        var directory = head?.Directory;

        // Messages
        if (!(await Opencode.GetMessagesAsync(SessionId.Id)).TryGetValue(out var messages, out var error))
        {
            Toasts.ShowError(error, "Could not load messages");
            return;
        }
        foreach (var msg in messages)
        {
            var message = MessageJsonHelper.MessageFromJson(msg);
            if (message is null) continue;
            _messagesById[message.Id] = message;
            AppendMessage(message);
        }
        UpdateSessionStats();

        // Pending permissions
        if (directory is not null)
            await SyncPendingPermissionsAsync(directory);

        // Pending questions (attach to existing tool parts)
        if (directory is not null)
            await SyncPendingQuestionsAsync(directory);
    }

    // ── Message collection management ───────────────────────────────────────

    void AppendMessage(MessageItem message)
    {
        Messages.Add(message);
        while (Messages.Count > MaxVisibleMessages)
        {
            Messages.RemoveAt(0);
            TruncatedMessagesCount++;
        }
    }

    // ── SSE event registration ──────────────────────────────────────────────

    void RegisterEvents()
    {
        Events.RegisterMessageUpdated(null, OnMessageUpdated);
        Events.RegisterMessagePartUpdated(null, OnPartUpdated);
        Events.RegisterMessagePartDelta(null, OnPartDelta);
        Events.RegisterMessagePartRemoved(null, OnPartRemoved);
        Events.RegisterMessageRemoved(null, OnMessageRemoved);
        Events.RegisterSessionUpdated(null, OnSessionUpdated);
        Events.RegisterSessionStatus(null, OnSessionStatus);
        Events.RegisterPermissionAsked(null, OnPermissionAsked);
        Events.RegisterPermissionReplied(null, OnPermissionReplied);
        Events.RegisterQuestionAsked(null, OnQuestionAsked);
        Events.RegisterQuestionReplied(null, OnQuestionReplied);
        Events.RegisterQuestionRejected(null, OnQuestionRejected);
    }

    void UnregisterEvents()
    {
        Events.UnregisterMessageUpdated(null, OnMessageUpdated);
        Events.UnregisterMessagePartUpdated(null, OnPartUpdated);
        Events.UnregisterMessagePartDelta(null, OnPartDelta);
        Events.UnregisterMessagePartRemoved(null, OnPartRemoved);
        Events.UnregisterMessageRemoved(null, OnMessageRemoved);
        Events.UnregisterSessionUpdated(null, OnSessionUpdated);
        Events.UnregisterSessionStatus(null, OnSessionStatus);
        Events.UnregisterPermissionAsked(null, OnPermissionAsked);
        Events.UnregisterPermissionReplied(null, OnPermissionReplied);
        Events.UnregisterQuestionAsked(null, OnQuestionAsked);
        Events.UnregisterQuestionReplied(null, OnQuestionReplied);
        Events.UnregisterQuestionRejected(null, OnQuestionRejected);
    }

    // ── SSE event handlers ──────────────────────────────────────────────────

    void OnMessageUpdated(string _, MessageUpdatedEvent e)
    {
        if (e.SessionId != SessionId.Id) return;
        if (e.Info is not MessageInfo info) return;
        var id = info switch
        {
            AssistantMessageInfo a => a.Id,
            UserMessageInfo u => u.Id,
            _ => "",
        };
        if (id.Length == 0) return;

        if (_messagesById.TryGetValue(id, out var message))
        {
            var role = info switch
            {
                AssistantMessageInfo => "assistant",
                UserMessageInfo => "user",
                _ => "",
            };
            if (role.Length > 0) message.Role = role;
            MessageJsonHelper.ApplyMessageStats(message, info);
            if (info is AssistantMessageInfo assist)
            {
                if (MessageJsonHelper.IsAbortedError(assist) && !message.Parts.Any(p => p.Type == "aborted"))
                {
                    message.Interrupted = true;
                    message.Parts.Add(new AbortedPartItem { Id = $"aborted-{id}", MessageId = id });
                }
                else
                {
                    MessageJsonHelper.ApplyMessageError(message, assist);
                }
            }
            UpdateSessionStats();
            return;
        }

        message = new MessageItem
        {
            Id = id,
            Role = info switch
            {
                AssistantMessageInfo => "assistant",
                UserMessageInfo => "user",
                _ => "",
            },
            Agent = info switch
            {
                AssistantMessageInfo a => a.Agent,
                UserMessageInfo u => u.Agent,
                _ => "",
            },
        };
        MessageJsonHelper.ApplyMessageStats(message, info);
        if (info is AssistantMessageInfo assist2)
        {
            if (MessageJsonHelper.IsAbortedError(assist2))
            {
                message.Interrupted = true;
                message.Parts.Add(new AbortedPartItem { Id = $"aborted-{id}", MessageId = id });
            }
            else
            {
                MessageJsonHelper.ApplyMessageError(message, assist2);
            }
        }
        _messagesById[id] = message;
        AppendMessage(message);
        UpdateSessionStats();
    }

    void OnPartUpdated(string _, MessagePartUpdatedEvent e)
    {
        if (e.SessionId != SessionId.Id) return;
        var part = e.Part;
        if (!_messagesById.TryGetValue(part.MessageId, out var message)) return;

        var existing = message.Parts.FirstOrDefault(p => p.Id == part.Id);
        if (existing is null)
        {
            if (part is StepStartPart or StepFinishPart) return;
            var p = MessageJsonHelper.PartFromPart(part);
            if (p.Type == "text" && p is TextPartItem text && text.Synthetic && message.Parts.Count == 0)
            {
                Messages.Remove(message);
                return;
            }
            message.Parts.Add(p);
            if (p is FilePartItem file)
                AsyncHelper.RunAndReport(file.LoadImageAsync(),
                    Toasts, "", "Load image"
                );
            return;
        }

        var updated = MessageJsonHelper.PartFromPart(part);
        var idx = message.Parts.IndexOf(existing);
        message.Parts[idx] = updated;
        if (updated is FilePartItem fileUpdated)
            AsyncHelper.RunAndReport(fileUpdated.LoadImageAsync(),
                Toasts, "", "Load image"
            );
    }

    void OnPartDelta(string _, MessagePartDeltaEvent e)
    {
        if (e.SessionId != SessionId.Id) return;
        if (e.Field != "text" || e.Delta.Length == 0) return;
        if (!_messagesById.TryGetValue(e.MessageId, out var message)) return;
        var part = message.Parts.FirstOrDefault(p => p.Id == e.PartId);
        if (part is null) return;
        if (part is TextPartItem textPart)
            textPart.Text += e.Delta;
        else if (part is ReasoningPartItem reasoningPart)
            reasoningPart.Text += e.Delta;
    }

    void OnPartRemoved(string _, MessagePartRemovedEvent e)
    {
        if (e.SessionId != SessionId.Id) return;
        if (!_messagesById.TryGetValue(e.MessageId, out var message)) return;
        var part = message.Parts.FirstOrDefault(p => p.Id == e.PartId);
        if (part is not null) message.Parts.Remove(part);
    }

    void OnMessageRemoved(string _, MessageRemovedEvent e)
    {
        if (e.SessionId != SessionId.Id) return;
        if (e.MessageId.Length == 0) return;
        if (!_messagesById.TryGetValue(e.MessageId, out var message)) return;
        _messagesById.Remove(e.MessageId);
        Messages.Remove(message);
        UpdateSessionStats();
    }

    void OnSessionUpdated(string _, SessionCrudEvent e)
    {
        if (e.SessionId != SessionId.Id) return;
        var revertMsgId = e.Info.Revert?.MessageId ?? "";
        if (revertMsgId != RevertMessageId)
        {
            RevertMessageId = revertMsgId;
            RevertCount = ComputeRevertCount(revertMsgId);
        }
    }

    // ── Session status (retry / busy) ───────────────────────────────────────

    void OnSessionStatus(string _, SessionStatusEvent e)
    {
        if (e.SessionId != SessionId.Id) return;

        switch (e.Status)
        {
            case SessionStatusRetry retry:
                Retry = RetryState.From(retry);
                break;
            case SessionStatusBusy:
                Retry = RetryState.None;
                break;
            case SessionStatusIdle:
                Retry = RetryState.None;
                break;
        }
    }

    // ── Permissions ─────────────────────────────────────────────────────────

    void OnPermissionAsked(string _, PermissionAskedEvent e)
    {
        if (!IsSessionOrDescendant(e.SessionId)) return;

        var request = PermissionRequestItem.From(e);
        if (_permissions.Any(p => p.Id == request.Id)) return;
        _permissions.Add(request);
        UpdateActivePermission();
    }

    void OnPermissionReplied(string _, PermissionRepliedEvent e)
    {
        if (!IsSessionOrDescendant(e.SessionId)) return;
        _permissions.RemoveAll(p => p.Id == e.RequestId);
        UpdateActivePermission();
    }

    void UpdateActivePermission()
    {
        ActivePermission = _permissions.Count > 0 ? _permissions[0] : null;
    }

    bool IsSessionOrDescendant(string sessionId)
    {
        var current = sessionId;
        var guard = 0;
        while (current.Length > 0 && guard++ < 64)
        {
            if (current == SessionId.Id) return true;
            var head = Sessions.Head(new(current));
            current = head?.ParentId?.Id ?? "";
        }
        return false;
    }

    async Task SyncPendingPermissionsAsync(string directory)
    {
        try
        {
            if (!(await Opencode.GetPendingPermissionsAsync(directory)).TryGetValue(out var requests, out var error))
            {
                Toasts.ShowWarning(error, $"Could not sync approvals for {directory}");
                return;
            }

            _permissions.Clear();
            ActivePermission = null;
            foreach (var request in requests)
            {
                if (request.Id.Length == 0) continue;
                var item = PermissionRequestItem.From(request);
                if (!IsSessionOrDescendant(item.SessionId)) continue;
                _permissions.Add(item);
            }
            UpdateActivePermission();
        }
        catch (Exception ex)
        {
            Toasts.ShowError(ex.Message, "Could not sync approvals");
        }
    }

    public async Task ReplyPermissionAsync(string requestId, string reply, string? message = null)
    {
        var directory = Sessions.Head(SessionId)?.Directory;
        try
        {
            await Opencode.ReplyPermissionAsync(requestId, new() { Reply = reply, Message = message }, directory);
            _permissions.RemoveAll(p => p.Id == requestId);
            UpdateActivePermission();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _permissions.RemoveAll(p => p.Id == requestId);
            UpdateActivePermission();
            Toasts.ShowWarning("The permission request was already handled.", "Approval dismissed");
        }
        catch (Exception ex)
        {
            Toasts.ShowError(ex.Message, "Approval reply failed");
        }
    }

    // ── Questions ───────────────────────────────────────────────────────────

    void OnQuestionAsked(string _, QuestionAskedEvent e)
    {
        if (e.SessionId != SessionId.Id) return;
        if (e.Tool is null) return;
        if (!_messagesById.TryGetValue(e.Tool.MessageId, out var message)) return;
        var part = message.Parts.FirstOrDefault(p => p.CallId == e.Tool.CallId);
        if (part is null) return;

        part.QuestionRequestId = e.Id;
        if (e.Questions is { Count: > 0 })
        {
            part.Questions = e.Questions;
            MessageJsonHelper.PopulateQuestionForm(part, e.Questions);
        }
    }

    void OnQuestionReplied(string _, QuestionRepliedEvent e)
    {
        if (e.SessionId != SessionId.Id) return;
        ClearQuestionState(e.RequestId);
    }

    void OnQuestionRejected(string _, QuestionRejectedEvent e)
    {
        if (e.SessionId != SessionId.Id) return;
        ClearQuestionState(e.RequestId);
    }

    void ClearQuestionState(string requestId)
    {
        foreach (var message in Messages)
        {
            foreach (var part in message.Parts)
            {
                if (part is ToolCallPartItem tool && tool.QuestionRequestId == requestId)
                {
                    tool.QuestionRequestId = "";
                    tool.QuestionForm.Clear();
                }
            }
        }
    }

    async Task SyncPendingQuestionsAsync(string directory)
    {
        try
        {
            if (!(await Opencode.GetPendingQuestionsAsync(directory)).TryGetValue(out var questions, out var error))
            {
                Toasts.ShowWarning(error, "Could not sync questions");
                return;
            }

            foreach (var question in questions)
            {
                if (question.SessionId != SessionId.Id) continue;
                if (question.Tool is null) continue;
                var messageId = question.Tool.MessageId;
                var callId = question.Tool.CallId;
                if (messageId.Length == 0 || callId.Length == 0) continue;
                if (!_messagesById.TryGetValue(messageId, out var message)) continue;

                var part = message.Parts.FirstOrDefault(p => p.CallId == callId && p.ToolName == "question");
                if (part is null || part.QuestionRequestId.Length > 0) continue;

                part.QuestionRequestId = question.Id;
                if (question.Questions is { Count: > 0 })
                {
                    part.Questions = question.Questions;
                    MessageJsonHelper.PopulateQuestionForm(part, question.Questions);
                }
            }
        }
        catch (Exception ex)
        {
            Toasts.ShowError(ex.Message, "Could not sync questions");
        }
    }

    public async Task ReplyQuestionAsync(string requestId, IReadOnlyList<IReadOnlyList<string>> answers)
    {
        var directory = Sessions.Head(SessionId)?.Directory;
        try
        {
            await Opencode.ReplyQuestionAsync(requestId, new() { Answers = answers }, directory);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Toasts.ShowWarning("The question form was dismissed — the request is no longer pending.", "Question already handled");
        }
        catch (Exception ex)
        {
            Toasts.ShowError(ex.Message, "Question reply failed");
        }
    }

    public async Task RejectQuestionAsync(string requestId)
    {
        var directory = Sessions.Head(SessionId)?.Directory;
        try
        {
            await Opencode.RejectQuestionAsync(requestId, directory);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Toasts.ShowWarning("The question form was dismissed — the request is no longer pending.", "Question already handled");
        }
        catch (Exception ex)
        {
            Toasts.ShowError(ex.Message, "Question dismiss failed");
        }
    }

    // ── Stats computation ───────────────────────────────────────────────────

    void UpdateSessionStats()
    {
        var last = Messages.LastOrDefault(m => m.Role == "assistant" && m.TokensOutput > 0);
        if (last is null)
        {
            Cost = 0;
            Tokens = SessionTokens.Zero;
            ContextLimit = 0;
            return;
        }

        Cost = Messages.Where(m => m.Role == "assistant").Sum(m => m.Cost);
        Tokens = SessionTokens.From(last);
        ContextLimit = ResolveContextLimit(last);
    }

    long ResolveContextLimit(MessageItem message)
    {
        var model = Models.ModelOptions.FirstOrDefault(m => m.Id == message.ModelId
            && (message.ProviderId.Length == 0 || m.ProviderId == message.ProviderId));
        return model?.LimitContext ?? 0;
    }

    // ── Revert ──────────────────────────────────────────────────────────────

    public async Task RevertToMessageAsync(MessageItem message)
    {
        if (message is null) return;
        try
        {
            if (Sessions.Head(SessionId)?.IsBusy == true)
                await Opencode.AbortAsync(SessionId.Id);
            await Opencode.RevertAsync(SessionId.Id, new() { MessageID = message.Id });
            ApplyRevertMarker(message.Id);
        }
        catch (Exception ex)
        {
            Toasts.ShowError(ex.Message, "Revert failed");
        }
    }

    public async Task RedoLastMessageAsync()
    {
        if (RevertMessageId.Length == 0) return;
        try
        {
            var next = Messages
                .Where(m => m.Role == "user" && StringComparer.Ordinal.Compare(m.Id, RevertMessageId) > 0)
                .OrderBy(m => m.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (next is null)
            {
                await Opencode.UnrevertAsync(SessionId.Id);
                ApplyRevertMarker("");
                return;
            }
            await Opencode.RevertAsync(SessionId.Id, new() { MessageID = next.Id });
            ApplyRevertMarker(next.Id);
        }
        catch (Exception ex)
        {
            Toasts.ShowError(ex.Message, "Unrevert failed");
        }
    }

    public async Task UndoLastMessageAsync()
    {
        var target = FindUndoTargetMessage();
        if (target is not null) await RevertToMessageAsync(target);
    }

    MessageItem? FindUndoTargetMessage()
    {
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            var m = Messages[i];
            if (m.Role != "user") continue;
            if (RevertMessageId.Length > 0 && StringComparer.Ordinal.Compare(m.Id, RevertMessageId) >= 0) continue;
            return m;
        }
        return null;
    }

    void ApplyRevertMarker(string messageId)
    {
        RevertMessageId = messageId;
        RevertCount = ComputeRevertCount(messageId);
    }

    int ComputeRevertCount(string messageId)
    {
        if (messageId.Length == 0) return 0;
        return Messages.Count(m => m.Role == "user" && StringComparer.Ordinal.Compare(m.Id, messageId) >= 0);
    }

    // ── Dispose ─────────────────────────────────────────────────────────────

    public void Dispose()
    {
        UnregisterEvents();
        Messages.Clear();
        _messagesById.Clear();
        _permissions.Clear();
        ActivePermission = null;
        Retry = RetryState.None;
        GC.SuppressFinalize(this);
    }
}
