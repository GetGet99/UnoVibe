using UnoVibe.Integration;
using UnoVibe.Integration.Events;

namespace UnoVibe.Helpers;

enum PartUpsert
{
    IgnoredStep,
    Added,
    Updated,
    SyntheticEmpty,
}

static class MessageCollectionHelper
{
    public static List<MessageItem> ParseMessages(IEnumerable<MessageWithParts> source)
    {
        var parsed = new List<MessageItem>();
        foreach (var msg in source)
        {
            if (MessageJsonHelper.MessageFromJson(msg) is { } message)
                parsed.Add(message);
        }
        return parsed;
    }

    public static PartUpsert UpsertPart(MessageItem message, Part serverPart, out ChatPartItem? affected)
    {
        affected = null;
        if (serverPart is StepStartPart or StepFinishPart) return PartUpsert.IgnoredStep;
        var existing = message.Parts.FirstOrDefault(p => p.Id == serverPart.Id);
        if (existing is null)
        {
            var fresh = MessageJsonHelper.PartFromPart(serverPart);
            if (fresh.Type == "text" && fresh is TextPartItem text && text.Synthetic && message.Parts.Count == 0)
                return PartUpsert.SyntheticEmpty;
            message.Parts.Add(fresh);
            affected = fresh;
            return PartUpsert.Added;
        }
        if (existing is ToolCallPartItem existingTool && serverPart is ToolPart toolPart)
        {
            MessageJsonHelper.ApplyToolPart(existingTool, toolPart);
            affected = existingTool;
            return PartUpsert.Updated;
        }
        var updated = MessageJsonHelper.PartFromPart(serverPart);
        message.Parts[message.Parts.IndexOf(existing)] = updated;
        affected = updated;
        return PartUpsert.Updated;
    }

    public static void AppendDelta(MessageItem? message, MessagePartDeltaEvent e)
    {
        if (message is null) return;
        if (e.Field != "text" || e.Delta.Length == 0) return;
        var part = message.Parts.FirstOrDefault(p => p.Id == e.PartId);
        if (part is TextPartItem textPart)
            textPart.Text += e.Delta;
        else if (part is ReasoningPartItem reasoningPart)
            reasoningPart.Text += e.Delta;
    }

    public static void RemovePart(MessageItem? message, string partId)
    {
        var part = message?.Parts.FirstOrDefault(p => p.Id == partId);
        if (part is not null && message is not null)
            message.Parts.Remove(part);
    }

    public static bool AttachLiveQuestion(MessageItem? message, string callId, string requestId, IReadOnlyList<QuestionPayload> questions)
    {
        var part = message?.Parts.OfType<ToolCallPartItem>().FirstOrDefault(p => p.CallId == callId);
        if (part is null) return false;
        part.QuestionRequestId = requestId;
        if (questions is { Count: > 0 })
        {
            MessageJsonHelper.Refill(part.Questions, questions.Select(q => new QuestionInfo
            {
                Question = q.Question,
                Header = q.Header,
                Options = q.Options.Select(o => new QuestionOption { Label = o.Label, Description = o.Description }).ToList(),
                Multiple = q.Multiple ?? false,
                Custom = q.Custom ?? false,
            }));
            MessageJsonHelper.PopulateQuestionForm(part, part.Questions);
        }
        return true;
    }

    public static void AttachPendingQuestions(Func<string, MessageItem?> lookup, string sessionId, IEnumerable<PendingQuestion> questions)
    {
        foreach (var pending in questions)
        {
            if (pending.SessionId != sessionId || pending.Tool is null) continue;
            if (pending.Tool.MessageId.Length == 0 || pending.Tool.CallId.Length == 0) continue;
            var part = lookup(pending.Tool.MessageId)?.Parts.OfType<ToolCallPartItem>()
                .FirstOrDefault(p => p.CallId == pending.Tool.CallId && p.ToolName == "question");
            if (part is null || part.QuestionRequestId is { Length: > 0 }) continue;
            part.QuestionRequestId = pending.Id;
            if (pending.Questions is { Count: > 0 })
            {
                MessageJsonHelper.Refill(part.Questions, pending.Questions.Select(q => new QuestionInfo
                {
                    Question = q.Question,
                    Header = q.Header,
                    Options = q.Options.Select(o => new QuestionOption { Label = o.Label, Description = o.Description }).ToList(),
                    Multiple = q.Multiple,
                    Custom = q.Custom,
                }));
                MessageJsonHelper.PopulateQuestionForm(part, part.Questions);
            }
        }
    }

    public static void ClearQuestion(IEnumerable<MessageItem> messages, string requestId)
    {
        foreach (var message in messages)
        {
            foreach (var part in message.Parts)
            {
                if (part is ToolCallPartItem tool && tool.QuestionRequestId == requestId)
                {
                    tool.QuestionRequestId = null;
                    tool.QuestionForm.Clear();
                }
            }
        }
    }
}
