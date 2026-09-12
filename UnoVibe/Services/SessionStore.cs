using System.Text.Json;
using System.Threading.Channels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using UnoVibe.Models;
using UnoVibe.Providers;
using UnoVibe.Helpers;

namespace UnoVibe.Services;

/// <summary>
/// Reactive store for ONE chat session. Owns the session's messages, usage stats, revert
/// marker, retry/continue state, composer attachments and per-session mode/model/variant —
/// everything the chat page shows for the currently-active session.
///
/// Stores are created lazily by <see cref="ChatStore"/> the first time a session is opened
/// and cached (keyed by session id) so switching sessions never recreates or resets them:
/// switching re-points the router's <see cref="ChatStore.Active"/> reference and the cached
/// store (messages included) is reused. Sessions that exist on the sidebar but were never
/// opened have no store — only the router's per-session sidebar maps track them.
///
/// The mutable display fields are QuickMarkup reactive references (declared in the markup
/// header) so the chat page binds to them directly via <c>Store.Active.X</c>.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Models;
    using QuickMarkup.Infra.Collections;
    public int TruncatedMessagesCount;
    public SessionHead Head = `null!`;
    public string UsageCostLabel = "$0.00";
    public string UsageTokensLabel = "0";
    public string ContextLabel = "0%";
    public double ContextUsage;
    public long UsageTokensInput;
    public long UsageTokensOutput;
    public long UsageTokensReasoning;
    public long UsageTokensCacheRead;
    public long UsageTokensCacheWrite;
    public long ContextLimit;
    // Human-readable session status banner (busy/retry messages); empty means idle.
    public string StatusMessage = "";
    // Auto-retry state for the active turn (session.status type "retry"); drives the
    // end-of-chat retry card. RetryNextMs is the absolute unix-ms time of the next attempt.
    public bool IsRetrying;
    public string RetryMessage = "";
    public int RetryAttempt;
    public long RetryNextMs;
    // Live countdown text recomputed each second by the chat page timer ("Attempt #2 · retrying in 3s").
    public string RetryCountdown = "";
    // True when the stopped turn warrants the end-of-chat "Continue" button: the last assistant
    // message carried a non-interrupt error, or the chat ends on a Thinking (reasoning) part.
    // Never set when the stop was handled by an automatic "continue" (turn.autocontinue setting).
    public bool ShowContinue;
    public string Mode = "build";
    public string ModelId = "";
    public string ProviderId = "";
    public string? Variant;
    public bool HasVariants;
    // The ModelOption currently selected by the model combo. A computed derived from the
    // ModelId/ProviderId refs + the router's model list, so it re-resolves automatically
    // when the options are (re)populated (refresh rebuilds the option instances).
    public ModelOption? SelectedModelOption => `Router.ModelOptions.Reactive.FirstOrDefault(m => m.Id == ModelId && m.ProviderId == ProviderId)`;
    // Undo marker for this session: the id of the user message the conversation is
    // reverted to (the server's session "revert" field). Empty = not reverted. Drives the
    // revert card + message filter (messages with id >= RevertMessageId are hidden).
    public string RevertMessageId = "";
    // Card label for the revert banner, e.g. "1 message reverted". Computed whenever the
    // revert point changes (recounts the reverted user messages from the message list).
    public string RevertCountLabel = "";
    """)]
public sealed partial class SessionStore
{
    /// <summary>Maximum number of messages kept in the UI; older ones are dropped to keep rendering smooth.</summary>
    public const int MaxVisibleMessages = 200;

    /// <summary>The router that owns this store (client, sidebar, settings options).</summary>
    public ChatStore Router { get; set; } = null!;

    public ObservableCollection<MessageItem> Messages { get; } = new();

    private readonly Dictionary<string, MessageItem> _messagesById = new();

    private void AppendMessage(MessageItem message)
    {
        Messages.Add(message);
        while (Messages.Count > MaxVisibleMessages)
        {
            Messages.RemoveAt(0);
            TruncatedMessagesCount++;
        }
    }

    /// <summary>
    /// Full (awaited) load of this session's messages + settings. Called by the router the
    /// first time the session is opened. <paramref name="known"/> is the sidebar session
    /// when it's in the list (title/parent/model already known); a null it falls back to
    /// <c>GET /session/:id</c> (e.g. a subagent whose session.created raced the click).
    /// </summary>
    public async Task LoadAsync(SessionInfoToRemove? known)
    {
        if (known is not null)
        {
            ApplySessionSettings(known);
        }
        else
        {
            await LoadInfoAsync();
        }
        await LoadMessagesAsync();
        await Router.SyncPendingQuestionsAsync();
    }

    /// <summary>
    /// Background refresh of a cached store's messages (stale-while-revalidate) so a revisit
    /// shows fresh content. Skips the swap while the session is busy, so an in-flight turn's
    /// streaming deltas are never clobbered by a snapshot taken mid-stream.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (Router.IsSessionBusy(Head.Id)) return;
        await LoadMessagesAsync();
        await Router.SyncPendingQuestionsAsync();
    }

    /// <summary>Fetches and replaces this store's message list from GET /session/:id.</summary>
    private async Task LoadMessagesAsync()
    {
        Messages.Clear();
        _messagesById.Clear();
        TruncatedMessagesCount = 0;
        if (!(await Router.Client.GetMessagesAsync(Head.Id)).TryGetValue(out var messages, out var error))
        {
            Router.ShowError(error, "Could not load messages");
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

    /// <summary>
    /// Undoes the agent's reply to the last user message. Mirrors the TUI's <c>session.undo</c>
    /// command: aborts if the session is busy (the server 409s a revert while busy), targets the
    /// last user message before the current revert point (so a second undo walks further back),
    /// calls POST /session/{id}/revert, and restores the undone user prompt (text + staged
    /// images) into the composer. No message refetch is needed — the server keeps reverted
    /// messages until the next prompt, and the chat page hides messages at/after the revert
    /// point via <see cref="RevertMessageId"/>.
    /// </summary>
    public async Task UndoLastMessageAsync()
    {
        if (Head.Id.Length == 0) return;
        var target = FindUndoTargetMessage();
        if (target is null) return;
        await RevertToMessageAsync(target);
    }

    /// <summary>
    /// Reverts the conversation to a specific user message ("undo to here"), mirroring the web
    /// client's per-message revert action and the TUI's message dialog "Revert". Aborts if the
    /// session is busy, calls POST /session/{id}/revert for the target message, and restores
    /// that message's prompt (text + staged images) into the composer. Messages at/after the
    /// target are hidden via <see cref="RevertMessageId"/> (the target itself included).
    /// </summary>
    public async Task RevertToMessageAsync(MessageItem message)
    {
        if (Head.Id.Length == 0 || message is null) return;
        try
        {
            if (Head.IsBusy) await Router.Client.AbortAsync(Head.Id);

            await Router.Client.RevertAsync(Head.Id, new() { MessageID = message.Id });

            RestorePromptFromMessage(message);
            ApplyRevertMarker(message.Id);
        }
        catch (Exception ex)
        {
            Router.ShowError(ex.Message, "Revert failed");
        }
    }

    /// <summary>
    /// Restores reverted messages. If a user message exists beyond the revert point, reverts
    /// forward to it; otherwise clears the revert entirely (unrevert). Mirrors the TUI's
    /// <c>session.redo</c> command.
    /// </summary>
    public async Task RedoLastMessageAsync()
    {
        if (Head.Id.Length == 0 || RevertMessageId.Length == 0) return;
        try
        {
            var next = Messages
                .Where(m => m.Role == "user" && StringComparer.Ordinal.Compare(m.Id, RevertMessageId) > 0)
                .OrderBy(m => m.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (next is null)
            {
                await Router.Client.UnrevertAsync(Head.Id);
                ResetRevertState();
                return;
            }

            await Router.Client.RevertAsync(Head.Id, new() { MessageID = next.Id });
            ApplyRevertMarker(next.Id);
        }
        catch (Exception ex)
        {
            Router.ShowError(ex.Message, "Unrevert failed");
        }
    }

    /// <summary>
    /// The next undo target: the last user message strictly before the current revert point
    /// (a second undo walks further back), or the last user message overall when nothing is
    /// reverted yet. Null when there is nothing left to undo.
    /// </summary>
    private MessageItem? FindUndoTargetMessage()
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

    /// <summary>Sets the revert point and recomputes the card label.</summary>
    private void ApplyRevertMarker(string messageId)
    {
        RevertMessageId = messageId;
        RevertCountLabel = ComputeRevertCountLabel(messageId);
    }

    /// <summary>
    /// "N message(s) reverted" — counts the reverted user messages (id &gt;= the revert
    /// point, both user and assistant messages are hidden from view but only user messages
    /// are counted, matching the TUI's reverted-count).
    /// </summary>
    private string ComputeRevertCountLabel(string messageId)
    {
        if (messageId.Length == 0) return "";
        var count = Messages.Count(m => m.Role == "user" && StringComparer.Ordinal.Compare(m.Id, messageId) >= 0);
        return count == 1 ? "1 message reverted" : $"{count} messages reverted";
    }

    internal void ApplyMessageUpdated(JsonElement properties)
    {
        if (!properties.TryGetProperty("info", out var info)) return;
        var id = info.GetStringProperty("id");
        if (id.Length == 0) return;

        if (_messagesById.TryGetValue(id, out var message))
        {
            var role = info.GetStringProperty("role");
            if (role.Length > 0) message.Role = role;
            ApplyMessageStats(message, info);
            if (MarkInterrupted(message, info)) ShowContinue = false;
            ApplyMessageError(message, info);
            // The server emits session.status idle and this final message.updated (carrying
            // finish/error) in either order; both are turn-stop signals handled uniformly
            // (auto-continue or the Continue button). While an auto-continue is awaiting its
            // restarted turn, a trailing finish echo must not clobber the fresh busy state.
            if (!AwaitingAutoContinueRun && info.TryGetProperty("finish", out _)) OnTurnCompleted();
            if (!Head.IsBusy) HandleStoppedTurn();
            UpdateSessionStats();
            return;
        }

        message = new MessageItem
        {
            Id = id,
            Role = info.GetStringProperty("role"),
            Agent = info.GetStringProperty("agent"),
        };
        ApplyMessageStats(message, info);
        if (MarkInterrupted(message, info)) ShowContinue = false;
        ApplyMessageError(message, info);
        if (!AwaitingAutoContinueRun && info.TryGetProperty("finish", out _)) OnTurnCompleted();
        if (!Head.IsBusy) HandleStoppedTurn();
        _messagesById[id] = message;
        AppendMessage(message);
        UpdateSessionStats();
    }

    /// <summary>
    /// Appends a reactive "aborted" marker part when the message carries an abort error.
    /// Returns true when this call transitioned the message to interrupted (the marker was
    /// newly added) — the caller uses it to drop a Continue state that was decided before the
    /// abort error arrived (the idle/finish stop signals can precede it).
    /// </summary>
    private static bool MarkInterrupted(MessageItem message, JsonElement info)
    {
        if (!IsAbortedError(info)) return false;
        if (message.Parts.Any(p => p.Type == "aborted")) return false;
        message.Interrupted = true;
        message.Parts.Add(new PartItem
        {
            Id = $"aborted-{Guid.NewGuid():N}",
            MessageId = message.Id,
            Type = "aborted",
        });
        return true;
    }

    /// <summary>
    /// True when this session's most recent assistant message carries a non-interrupt
    /// error part (i.e. the last turn stopped with an error). Interrupts are MessageAbortedError
    /// → aborted part, not an error part, so they never qualify.
    /// </summary>
    private bool LastAssistantMessageErrored()
    {
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            var m = Messages[i];
            if (m.Role != "assistant") continue;
            return m.Parts.Any(p => p.Type == "error");
        }
        return false;
    }

    /// <summary>
    /// True when this session's most recent assistant message ends on a "reasoning" (thinking)
    /// part — the visible chat ends on a Thinking block. A turn that stops while the model is
    /// still thinking (stream failure mid-reasoning, or a reasoning-only finish) often leaves
    /// no error part to latch onto, so this catches the case <see cref="LastAssistantMessageErrored"/>
    /// misses.
    /// </summary>
    private bool LastAssistantMessageEndsOnThinking()
    {
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            var m = Messages[i];
            if (m.Role != "assistant") continue;
            if (m.Parts.Count == 0) return false;
            return m.Parts[^1].Type == "reasoning";
        }
        return false;
    }

    /// <summary>
    /// True when the stopped turn warrants the end-of-chat "Continue" button: the last assistant
    /// message carries an error part, or the chat ends on a Thinking (reasoning) part. Aborted
    /// turns never qualify (interrupt → "aborted" part, not an error part or a trailing Thinking).
    /// </summary>
    private bool ShouldShowContinue() =>
        LastAssistantMessageErrored() || LastAssistantMessageEndsOnThinking();

    /// <summary>
    /// True when this session's most recent assistant message was interrupted by the user
    /// (abort). Guards the auto-continue against a stop signal racing the aborted part: a user
    /// Stop must never be answered with an automatic "continue". <see cref="interruptRequested"/>
    /// covers the ordering where session.status idle is handled before the aborted marker lands.
    /// </summary>
    private bool LastAssistantMessageInterrupted()
    {
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            var m = Messages[i];
            if (m.Role != "assistant") continue;
            return m.Interrupted || m.Parts.Any(p => p.Type == "aborted");
        }
        return false;
    }

    /// <summary>Runs when the active turn ends (message finished or session idle).</summary>
    private void OnTurnCompleted()
    {
        Head.IsBusy = false;
        _ = ChatboxSource[Head.Id]?.DrainPendingAsync();
    }

    /// <summary>
    /// Recomputes the live countdown for the end-of-chat retry card. The chat page ticks this
    /// once per second while a turn is auto-retrying (<see cref="RetryNextMs"/> is the absolute
    /// unix-ms time the server will fire the next attempt at).
    /// </summary>
    public void UpdateRetryCountdown()
    {
        if (!IsRetrying)
        {
            RetryCountdown = "";
            return;
        }
        if (RetryNextMs <= 0)
        {
            RetryCountdown = RetryAttempt > 0 ? $"Attempt #{RetryAttempt} · retrying…" : "Retrying…";
            return;
        }
        var seconds = Math.Max(0, (int)Math.Ceiling((RetryNextMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000.0));
        RetryCountdown = RetryAttempt > 0 ? $"Attempt #{RetryAttempt} · retrying in {seconds}s" : $"Retrying in {seconds}s";
    }

    internal void ApplyPartUpdated(JsonElement properties)
    {
        if (!properties.TryGetProperty("part", out var part)) return;
        if (!_messagesById.TryGetValue(part.GetStringProperty("messageID"), out var message)) return;

        var partId = part.GetStringProperty("id");
        var existing = message.Parts.FirstOrDefault(p => p.Id == partId);
        if (existing is null)
        {
            if (part.GetStringProperty("type") is "step-start" or "step-finish") return;
            var p = MessageJsonHelper.PartFromJson(part);
            if (p.Synthetic && p.Type == "text" && message.Parts.Count == 0)
            {
                Messages.Remove(message);
                return;
            }
            message.Parts.Add(p);
            _ = p.LoadImageAsync();
            return;
        }

        UpdatePart(existing, part);
    }

    internal void ApplyPartDelta(JsonElement properties)
    {
        var messageId = properties.GetStringProperty("messageID");
        var partId = properties.GetStringProperty("partID");
        var field = properties.GetStringProperty("field");
        var delta = properties.GetStringProperty("delta");
        if (field != "text" || delta.Length == 0) return;
        if (!_messagesById.TryGetValue(messageId, out var message)) return;

        var part = message.Parts.FirstOrDefault(p => p.Id == partId);
        if (part is null) return;
        part.Text += delta;
    }

    internal void ApplyPartRemoved(JsonElement properties)
    {
        var messageId = properties.GetStringProperty("messageID");
        var partId = properties.GetStringProperty("partID");
        if (!_messagesById.TryGetValue(messageId, out var message)) return;

        var part = message.Parts.FirstOrDefault(p => p.Id == partId);
        if (part is not null) message.Parts.Remove(part);
    }

    /// <summary>
    /// Drops a message from the UI. The server emits this when a reverted session's messages
    /// are cleaned up at the start of the next prompt (SessionRevert.cleanup), and for other
    /// message removals. The message is scoped to this session (the router dispatches by
    /// sessionID), so only this store's list is touched.
    /// </summary>
    internal void ApplyMessageRemoved(JsonElement properties)
    {
        var id = properties.GetStringProperty("messageID");
        if (id.Length == 0) return;
        if (!_messagesById.TryGetValue(id, out var message)) return;

        _messagesById.Remove(id);
        Messages.Remove(message);
        UpdateSessionStats();
    }

    /// <summary>
    /// Applies a <c>session.status</c> event for THIS session only (the router forwards it).
    /// Handles the active banner (busy/retry) and the Continue button; sidebar maps are owned
    /// by the router.
    /// </summary>
    internal void ApplySessionStatus(JsonElement properties)
    {
        if (!properties.TryGetProperty("status", out var status)) return;
        var type = status.GetStringProperty("type");

        Head.IsBusy = type != "idle";
        if (type != "idle")
        {
            sawRunningStatus = true;
            interruptRequested = false;
        }

        if (type == "retry")
        {
            var message = status.GetStringProperty("message");
            var attempt = status.GetInt64Property("attempt");
            var next = status.GetInt64Property("next");
            var prefix = attempt > 0 ? $"Retry #{attempt}" : "Retry";
            StatusMessage = message.Length > 0 ? $"{prefix}: {message}" : prefix;

            IsRetrying = true;
            RetryMessage = message;
            RetryAttempt = (int)attempt;
            RetryNextMs = next;
            UpdateRetryCountdown();
        }
        else
        {
            StatusMessage = "";
            IsRetrying = false;
            RetryMessage = "";
            RetryAttempt = 0;
            RetryNextMs = 0;
            RetryCountdown = "";

            // The turn finished. If it stopped because of a non-interrupt error, or with the
            // chat left ending on a Thinking part, surface the "Continue" button — or, when the
            // auto-continue-on-thinking-stop setting is on and the stop qualifies, send the
            // "continue" prompt instead (HandleStoppedTurn). (Interrupts are MessageAbortedError
            // → aborted part instead.)
            if (type == "idle") HandleStoppedTurn();
        }

        if (!Head.IsBusy) _ = DrainPendingPromptsAsync();
    }

    /// <summary>
    /// Re-syncs the pending-question request IDs for this session's tool parts after a reload
    /// (requestIDs only exist in the live question.asked event and the server's in-memory
    /// pending map, not in the persisted message parts).
    /// </summary>
    internal void AttachQuestionRequest(Integration.PendingQuestion question)
    {
        if (question.Tool is null) return;
        var messageId = question.Tool.MessageId;
        var callId = question.Tool.CallId;
        if (messageId.Length == 0 || callId.Length == 0) return;
        if (!_messagesById.TryGetValue(messageId, out var message)) return;

        var part = message.Parts.FirstOrDefault(p => p.CallId == callId && p.ToolName == "question");
        if (part is null || part.QuestionRequestId.Length > 0) return;

        AttachQuestion(part, question.Id, question);
    }

    /// <summary>Applies a live <c>question.asked</c> event to this session's tool part.</summary>
    internal void ApplyQuestionAsked(JsonElement properties)
    {
        var requestId = properties.GetStringProperty("id");
        if (requestId.Length == 0) return;

        if (!properties.TryGetProperty("tool", out var tool)) return;
        var messageId = tool.GetStringProperty("messageID");
        var callId = tool.GetStringProperty("callID");
        if (messageId.Length == 0 || callId.Length == 0) return;

        if (!_messagesById.TryGetValue(messageId, out var message)) return;
        var part = message.Parts.FirstOrDefault(p => p.CallId == callId);
        if (part is null) return;

        AttachQuestion(part, requestId, properties);
    }

    private static void AttachQuestion(PartItem part, string requestId, JsonElement properties)
    {
        part.QuestionRequestId = requestId;
        if (properties.TryGetProperty("questions", out var questions) && questions.ValueKind == JsonValueKind.Array)
        {
            part.Questions = questions.Deserialize(AppJsonContext.Default.ListQuestionInfo)!;
            PopulateQuestionForm(part, part.Questions);
        }
    }

    private static void AttachQuestion(PartItem part, string requestId, Integration.PendingQuestion properties)
    {
        part.QuestionRequestId = requestId;
        if (properties.Questions is not null)
        {
            part.Questions = properties.Questions;
            PopulateQuestionForm(part, properties.Questions);
        }
    }

    /// <summary>
    /// Applies a <c>session.updated</c>/<c>session.created</c> event's info to this store:
    /// keeps the title, parent id and model settings current, and syncs the revert marker
    /// (the server omits "revert" entirely on unrevert).
    /// </summary>
    internal void ApplySessionInfo(SessionInfoToRemove session, JsonElement info)
    {
        if (session.ModelId.Length > 0)
        {
            ModelId = session.ModelId;
            ProviderId = session.ModelProviderId;
            UpdateVariantOptions();
            Variant = session.ModelVariant is "" or "default" or null ? null : session.ModelVariant;
            ReapplyComboSelections();
        }

        var revertMessageId = "";
        if (info.TryGetProperty("revert", out var revert) && revert.ValueKind == JsonValueKind.Object)
            revertMessageId = revert.GetStringProperty("messageID");
        if (revertMessageId != RevertMessageId)
        {
            if (revertMessageId.Length == 0) RevertPromptText = "";
            ApplyRevertMarker(revertMessageId);
        }
    }

    private void ResetUsageStats()
    {
        UsageCostLabel = "$0.00";
        UsageTokensLabel = "0";
        ContextLabel = "0%";
        ContextUsage = 0;
        UsageTokensInput = 0;
        UsageTokensOutput = 0;
        UsageTokensReasoning = 0;
        UsageTokensCacheRead = 0;
        UsageTokensCacheWrite = 0;
        ContextLimit = 0;
    }

    private void UpdateSessionStats()
    {
        var last = Messages.LastOrDefault(m => m.Role == "assistant" && m.TokensOutput > 0);
        if (last is null)
        {
            ResetUsageStats();
            return;
        }

        var sessionCost = Messages.Where(m => m.Role == "assistant").Sum(m => m.Cost);
        UsageCostLabel = FormatCost(sessionCost);

        UsageTokensInput = last.TokensInput;
        UsageTokensOutput = last.TokensOutput;
        UsageTokensReasoning = last.TokensReasoning;
        UsageTokensCacheRead = last.TokensCacheRead;
        UsageTokensCacheWrite = last.TokensCacheWrite;

        var tokens = last.TokensInput + last.TokensOutput + last.TokensReasoning
            + last.TokensCacheRead + last.TokensCacheWrite;
        UsageTokensLabel = tokens.ToString("N0");

        var limit = ResolveContextLimit(last);
        ContextLimit = limit;
        if (limit > 0)
        {
            var percent = (int)Math.Round(tokens / (double)limit * 100);
            ContextLabel = $"{percent}%";
            ContextUsage = percent;
        }
        else
        {
            ContextLabel = "--";
            ContextUsage = 0;
        }
    }

    private long ResolveContextLimit(MessageItem message)
    {
        var model = Router.ModelOptions.FirstOrDefault(m => m.Id == message.ModelId
            && (message.ProviderId.Length == 0 || m.ProviderId == message.));
        model ??= Router.ModelOptions.FirstOrDefault(m => m.Id == ModelId && m.ProviderId == ProviderId);
        return model?.LimitContext ?? 0;
    }

    private static string FormatCost(double cost)
    {
        if (cost <= 0) return "$0.00";
        if (cost < 0.01) return $"${cost:0.####}";
        return $"${cost:F2}";
    }

    private static void UpdatePart(PartItem item, JsonElement part)
    {
        if (item.Type is "text" or "reasoning" && part.TryGetProperty("text", out var text))
            item.Text = text.GetString() ?? "";

        if (item.Type == "reasoning" && part.TryGetProperty("time", out var time))
            item.Time = MessageJsonHelper.ParsePartTime(time);

        if (item.Type == "reasoning" || item.Type == "text")
            item.Synthetic = part.GetBoolProperty("synthetic", item.Synthetic);

        if (item.Type == "tool")
            ApplyToolState(item, part);

        if (item.Type == "file")
        {
            item.Mime = part.GetStringProperty("mime");
            item.Url = part.GetStringProperty("url");
            item.FileName = part.GetStringProperty("filename") != "" ? part.GetStringProperty("filename") : item.Url;
        }
    }

    // ---------------------------------------------------------------------
    // Mode / model / variant (per-session agent settings).
    // ---------------------------------------------------------------------

    private void ApplySessionSettings(SessionInfoToRemove session)
    {
        if (session.Agent.Length > 0) Mode = session.Agent;
        if (session.ModelId.Length > 0)
        {
            ModelId = session.ModelId;
            ProviderId = session.ModelProviderId;
        }
        UpdateVariantOptions();
        Variant = session.ModelVariant is "" or "default" or null ? null : session.ModelVariant;
        ReapplyComboSelections();
    }

    // Reference.Value only fires when the value changes; the SelectedItem bindings ran once
    // against empty options, so nudge the refs to make the bindings re-apply the selection.
    internal void ReapplyComboSelections()
    {
        var mode = Mode; Mode = ""; Mode = mode;
        var modelId = ModelId; ModelId = ""; ModelId = modelId;
        var variant = Variant; Variant = null; Variant = variant;
    }

    internal void UpdateVariantOptions()
    {
        Router.VariantOptions.Clear();
        Router.VariantOptions.Add("default");
        var model = Router.ModelOptions.FirstOrDefault(m => m.Id == ModelId && m.ProviderId == ProviderId);
        if (model is not null)
            foreach (var v in model.Variants) Router.VariantOptions.Add(v);
        HasVariants = model?.Variants.Length > 0;
        if (Variant != "default" && !Router.VariantOptions.Contains(Variant)) Variant = null;
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
            if (!(await _client.GetPendingQuestionsAsync(Head.Directory)).TryGetValue(out var questions, out var error))
            {
                ShowError(error, "Could not sync questions");
            }

            foreach (var question in questions)
            {
                if (question.SessionId == Head.Id)
                    AttachQuestionRequest(question);
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message, "Could not sync questions");
        }
    }
}
