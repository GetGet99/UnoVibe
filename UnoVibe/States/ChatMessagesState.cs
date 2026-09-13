using UnoVibe.Helpers;
using UnoVibe.Integration;
using UnoVibe.Integration.Events;
using UnoVibe.Providers;

namespace UnoVibe.States;

/// <summary>
/// Session-scoped reactive state owning the message list, cost/tokens, and revert marker
/// for a single chat session. Created by <see cref="Pages.Chat.ChatPage"/> when the active
/// session changes and disposed when switching away.
///
/// Chat UI components (<c>ChatMessageList</c>, <c>ChatCost</c>, <c>ChatCostInline</c>)
/// read from this state. Formatting is the caller's responsibility — this state stores raw
/// numeric values.
/// </summary>
[QuickMarkup("""
    double Cost;
    SessionTokens Tokens = `SessionTokens.Zero`;
    long ContextLimit;
    int TruncatedMessagesCount;
    string RevertMessageId = "";
    int RevertCount;
    """)]
partial class ChatMessagesState : IDisposable
{
    /// <summary>Maximum number of messages kept in the UI; older ones are dropped for rendering performance.</summary>
    public const int MaxVisibleMessages = 200;

    public SessionId SessionId { get; private set; }
    public ObservableCollection<MessageItem> Messages { get; } = [];
    readonly Dictionary<string, MessageItem> _messagesById = new();

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
    }

    void UnregisterEvents()
    {
        Events.UnregisterMessageUpdated(null, OnMessageUpdated);
        Events.UnregisterMessagePartUpdated(null, OnPartUpdated);
        Events.UnregisterMessagePartDelta(null, OnPartDelta);
        Events.UnregisterMessagePartRemoved(null, OnPartRemoved);
        Events.UnregisterMessageRemoved(null, OnMessageRemoved);
        Events.UnregisterSessionUpdated(null, OnSessionUpdated);
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
        GC.SuppressFinalize(this);
    }
}
