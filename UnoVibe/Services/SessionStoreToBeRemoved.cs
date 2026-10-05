using System.Text.Json;
using UnoVibe.Models;

namespace UnoVibe.Services;

[QuickMarkup("""
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
    public string StatusMessage = "";
    public bool IsRetrying;
    public string RetryMessage = "";
    public int RetryAttempt;
    public long RetryNextMs;
    public string RetryCountdown = "";
    public bool ShowContinue;
    public string Mode = "build";
    public string ModelId = "";
    public string ProviderId = "";
    public string? Variant;
    public bool HasVariants;
    public ModelOption? SelectedModelOption => `Router.ModelOptions.Reactive.FirstOrDefault(m => m.Id == ModelId && m.ProviderId == ProviderId)`;
    public string RevertMessageId = "";
    public string RevertCountLabel = "";
    """)]
[Obsolete("This class will be removed", error: true)]
public sealed partial class SessionStoreToBeRemoved
{
    public const int MaxVisibleMessages = 200;

    public ChatStoreToBeRemoved Router { get; set; } = null!;

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

    public async Task RefreshAsync()
    {
        if (Router.IsSessionBusy(Head.Id)) return;
        await LoadMessagesAsync();
        await Router.SyncPendingQuestionsAsync();
    }

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

    public async Task UndoLastMessageAsync()
    {
        if (Head.Id.Length == 0) return;
        var target = FindUndoTargetMessage();
        if (target is null) return;
        await RevertToMessageAsync(target);
    }

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

    private void ApplyRevertMarker(string messageId)
    {
        RevertMessageId = messageId;
        RevertCountLabel = ComputeRevertCountLabel(messageId);
    }

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

    private bool ShouldShowContinue() =>
        LastAssistantMessageErrored() || LastAssistantMessageEndsOnThinking();

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

    private void OnTurnCompleted()
    {
        Head.IsBusy = false;
        _ = ChatboxSource[Head.Id]?.DrainPendingAsync();
    }

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

    internal void ApplyMessageRemoved(JsonElement properties)
    {
        var id = properties.GetStringProperty("messageID");
        if (id.Length == 0) return;
        if (!_messagesById.TryGetValue(id, out var message)) return;

        _messagesById.Remove(id);
        Messages.Remove(message);
        UpdateSessionStats();
    }

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

            if (type == "idle") HandleStoppedTurn();
        }

        if (!Head.IsBusy) _ = DrainPendingPromptsAsync();
    }

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
            && (message.ProviderId.Length == 0 || m.ProviderId == message.ProviderId));
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
