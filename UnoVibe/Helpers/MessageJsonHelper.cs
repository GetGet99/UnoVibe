using System.Text.Json;
using UnoVibe.Integration.Events;
using UnoVibe.Models;

namespace UnoVibe.Helpers;

static class MessageJsonHelper
{

    public static MessageItem? MessageFromJson(Integration.MessageWithParts msg)
    {
        if (msg.Info is null) return null;
        var info = msg.Info;
        var parts = msg.Parts;

        string id = info switch
        {
            AssistantMessageInfo a => a.Id,
            UserMessageInfo u => u.Id,
            _ => "",
        };
        if (id.Length == 0) return null;

        string role = info switch
        {
            AssistantMessageInfo => "assistant",
            UserMessageInfo => "user",
            _ => "",
        };
        string agent = info switch
        {
            AssistantMessageInfo a => a.Agent,
            UserMessageInfo u => u.Agent,
            _ => "",
        };

        var item = new MessageItem
        {
            Id = id,
            Role = role,
            Agent = agent,
        };
        ApplyMessageStats(item, info);
        if (parts is not null)
        {
            foreach (var part in parts)
            {
                if (part is StepStartPart or StepFinishPart) continue;
                var p = PartFromPart(part);
                if (p.Type == "text" && p.Synthetic) continue;
                if (p.Id.Length > 0) item.Parts.Add(p);
            }
        }
        if (info is AssistantMessageInfo assist && IsAbortedError(assist) && item.Parts.All(p => p.Type != "aborted"))
        {
            item.Interrupted = true;
            item.Parts.Add(new PartItem { Id = $"aborted-{item.Id}", MessageId = item.Id, Type = "aborted" });
        }
        else if (info is AssistantMessageInfo assist2)
        {
            ApplyMessageError(item, assist2);
        }
        if (item.Role == "user" && item.Parts.Count == 0) return null;
        LoadPartImages(item);
        return item;
    }

    public static PartItem PartFromPart(Part part)
    {
        var item = new PartItem
        {
            Id = part.Id,
            MessageId = part.MessageId,
        };

        switch (part)
        {
            case TextPart text:
                item.Type = "text";
                item.Text = text.Text;
                item.Synthetic = text.Synthetic ?? false;
                break;
            case ReasoningPart reasoning:
                item.Type = "reasoning";
                item.Text = reasoning.Text;
                item.Time = new ReasoningTime
                {
                    Start = (long)reasoning.Time.Start,
                    End = reasoning.Time.End.HasValue ? (long)reasoning.Time.End.Value : 0,
                };
                break;
            case FilePart file:
                item.Type = "file";
                item.Mime = file.Mime;
                item.Url = file.Url;
                item.FileName = file.Filename ?? item.Url;
                break;
            case ToolPart tool:
                item.Type = "tool";
                item.CallId = tool.CallId;
                item.ToolName = tool.Tool;
                ApplyToolStateFromTyped(item, tool);
                break;
            case StepStartPart:
                item.Type = "step-start";
                break;
            case StepFinishPart:
                item.Type = "step-finish";
                break;
            case SnapshotPart snapshot:
                item.Type = "snapshot";
                break;
            case PatchPart patch:
                item.Type = "patch";
                item.Files = patch.Files.ToArray();
                break;
            case AgentPart agent:
                item.Type = "agent";
                break;
            case RetryPart retry:
                item.Type = "retry";
                item.Time = new ReasoningTime
                {
                    Start = (long)retry.Time.Created,
                };
                break;
            case CompactionPart compaction:
                item.Type = "compaction";
                break;
            case SubtaskPart subtask:
                item.Type = "subtask";
                break;
            default:
                item.Type = "unknown";
                break;
        }

        return item;
    }

    public static void ApplyMessageStats(MessageItem item, MessageInfo info)
    {
        if (info is not AssistantMessageInfo assistant) return;
        item.ModelId = assistant.ModelId;
        item.ProviderId = assistant.ProviderId;
        item.Cost = assistant.Cost;
        if (assistant.Tokens is { } tokens)
        {
            item.TokensInput = (long)tokens.Input;
            item.TokensOutput = (long)tokens.Output;
            item.TokensReasoning = (long)tokens.Reasoning;
            if (tokens.Cache is { } cache)
            {
                item.TokensCacheRead = (long)cache.Read;
                item.TokensCacheWrite = (long)cache.Write;
            }
        }
    }

    public static ChatOutcome ClassifyMessageOutcome(AssistantMessageInfo info)
    {
        if (info.Error is not {} error)
            return ChatOutcome.Success;
        return error is AbortedError ? ChatOutcome.Interrupted : ChatOutcome.Error;
    }

    public static bool IsAbortedError(AssistantMessageInfo info) => ClassifyMessageOutcome(info) is ChatOutcome.Error;

    public static void ApplyMessageError(MessageItem message, AssistantMessageInfo info)
    {
        if (info.Error is not { } error) return;
        if (error is AbortedError) return;
        if (message.Parts.Any(p => p.Type == "error")) return;

        string errorMessage = error switch
        {
            ProviderAuthError auth => auth.Message,
            UnknownAssistantError unknown => unknown.Message,
            ApiAssistantError api => api.Message,
            _ => "",
        };

        message.Parts.Add(new PartItem
        {
            Id = $"error-{message.Id}-{Guid.NewGuid():N}",
            MessageId = message.Id,
            Type = "error",
            ErrorName = error.GetType().Name,
            ErrorMessage = UnwrapErrorMessage(errorMessage),
        });
    }

    public static string UnwrapErrorMessage(string message)
    {
        message = message.Trim();
        if (message.Length >= 2 && message[0] == '"' && message[^1] == '"')
            message = message[1..^1];
        return message;
    }

    public static void ApplyToolStateFromTyped(PartItem item, ToolPart tool)
    {
        switch (tool.State)
        {
            case ToolStatePending pending:
                item.ToolStatus = "pending";
                break;
            case ToolStateRunning running:
                item.ToolStatus = "running";
                if (running.Title is { } title && title.Length > 0) item.ToolTitle = title;
                break;
            case ToolStateCompleted completed:
                item.ToolStatus = "completed";
                item.ToolTitle = completed.Title;
                item.ToolOutput = completed.Output;
                if (completed.Metadata is { } meta)
                {
                    ApplyToolMetadata(item, meta);
                    if (completed.Attachments is { } attachments)
                        item.Files = attachments.Select(a => a.Url).Where(u => u.Length > 0).ToArray();
                }
                break;
            case ToolStateError errorState:
                item.ToolStatus = "error";
                item.ToolError = errorState.Error;
                if (errorState.Metadata is { } meta)
                    ApplyToolMetadata(item, meta);
                break;
        }

        var input = tool.State.Input;
        if (input.ValueKind == JsonValueKind.Object)
        {
            var serialized = JsonSerializer.Serialize(input, AppJsonContext.Default.JsonElement);
            if (serialized != "{}") item.ToolInput = serialized;
            if (input.TryGetProperty("command", out var command)) item.ToolCommand = command.GetString() ?? "";
            if (input.TryGetProperty("filePath", out var filePath)) item.ToolFilePath = filePath.GetString() ?? "";
            if (input.TryGetProperty("content", out var content)) item.ToolContent = content.GetString() ?? "";
            if (input.TryGetProperty("pattern", out var pattern)) item.ToolPattern = pattern.GetString() ?? "";
            if (input.TryGetProperty("path", out var searchPath)) item.ToolSearchPath = searchPath.GetString() ?? "";
            if (input.TryGetProperty("include", out var include)) item.ToolInclude = include.GetString() ?? "";
            if (input.TryGetProperty("workdir", out var workdir)) item.ToolWorkdir = workdir.GetString() ?? "";
            if (input.TryGetProperty("url", out var url)) item.ToolUrl = url.GetString() ?? "";
            if (input.TryGetProperty("name", out var skillName)) item.ToolSkillName = skillName.GetString() ?? "";
            if (input.TryGetProperty("subagent_type", out var subType)) item.ToolSubagentType = subType.GetString() ?? "";
            if (input.TryGetProperty("todos", out var todos) && todos.ValueKind == JsonValueKind.Array)
                item.TodoJson = JsonSerializer.Serialize(todos, AppJsonContext.Default.JsonElement);
            if (input.TryGetProperty("questions", out var questions) && questions.ValueKind == JsonValueKind.Array)
            {
                item.Questions = questions.Deserialize(AppJsonContext.Default.ListQuestionInfo)!;
                PopulateQuestionForm(item, item.Questions);
            }
        }
    }

    private static void ApplyToolMetadata(PartItem item, Dictionary<string, JsonElement> meta)
    {
        if (meta.TryGetValue("interrupted", out var interm)) item.Interrupted = interm.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => interm.GetString() == "true",
            _ => item.Interrupted,
        };
        if (meta.TryGetValue("output", out var mOutput)) item.ShellOutput = mOutput.GetString() ?? "";
        if (meta.TryGetValue("diff", out var mDiff)) item.Diff = mDiff.GetString() ?? "";
        if (meta.TryGetValue("count", out var mCount)) item.MatchCount = mCount.ToString();
        if (meta.TryGetValue("matches", out var mMatches)) item.MatchCount = mMatches.ToString();
        if (meta.TryGetValue("loaded", out var mLoaded) && mLoaded.ValueKind == JsonValueKind.Array)
            item.LoadedFiles = string.Join("\n", mLoaded.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0));
        if (meta.TryGetValue("todos", out var mTodos) && mTodos.ValueKind == JsonValueKind.Array)
            item.TodoJson = JsonSerializer.Serialize(mTodos, AppJsonContext.Default.JsonElement);
        if (meta.TryGetValue("answers", out var mAnswers) && mAnswers.ValueKind == JsonValueKind.Array)
            item.AnswerJson = JsonSerializer.Serialize(mAnswers, AppJsonContext.Default.JsonElement);
        if (meta.TryGetValue("files", out var mFiles) && mFiles.ValueKind == JsonValueKind.Array)
            item.PatchJson = JsonSerializer.Serialize(mFiles, AppJsonContext.Default.JsonElement);
        if (meta.TryGetValue("sessionId", out var mSession)) item.ToolSessionId = mSession.GetString() ?? "";
        if (meta.TryGetValue("parentSessionId", out var mParent)) item.ToolParentSessionId = mParent.GetString() ?? "";
    }

    public static void PopulateQuestionForm(PartItem item, List<Integration.QuestionInfo> questions)
    {
        item.QuestionForm.Clear();
        foreach (var q in questions)
        {
            var form = new QuestionFormItem
            {
                Question = q.Question,
                Header = q.Header,
                AllowCustom = q.Custom,
                Multiple = q.Multiple,
            };

            foreach (var opt in q.Options)
            {
                form.Options.Add(new QuestionOptionItem
                {
                    Label = opt.Label,
                    Description = opt.Description,
                });
            }

            item.QuestionForm.Add(form);
        }
    }

    public static void LoadPartImages(MessageItem item)
    {
        foreach (var part in item.Parts)
        {
            if (part.Type == "file") _ = part.LoadImageAsync();
        }
    }
}
