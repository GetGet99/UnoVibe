using System.Text.Json;
using UnoVibe.Models;
using UnoVibe.Services;

namespace UnoVibe.Helpers;

static class MessageJsonHelper
{

    public static MessageItem? MessageFromJson(Integration.MessageWithParts msg)
    {
        if (msg.Info is null) return null;
        var info = msg.Info.Value;
        var parts = msg.Parts;

        if (info.GetStringProperty("id").Length == 0) return null;
        var item = new MessageItem
        {
            Id = info.GetStringProperty("id"),
            Role = info.GetStringProperty("role"),
            Agent = info.GetStringProperty("agent"),
        };
        ApplyMessageStats(item, info);
        if (parts is not null)
        {
            foreach (var part in parts)
            {
                if (part.GetStringProperty("type") is "step-start" or "step-finish") continue;
                var p = PartFromJson(part);
                if (p.Type == "text" && p.Synthetic) continue;
                if (p.Id.Length > 0) item.Parts.Add(p);
            }
        }
        if (IsAbortedError(info) && item.Parts.All(p => p.Type != "aborted"))
        {
            item.Interrupted = true;
            item.Parts.Add(new PartItem { Id = $"aborted-{item.Id}", MessageId = item.Id, Type = "aborted" });
        }
        else
        {
            ApplyMessageError(item, info);
        }
        if (item.Role == "user" && item.Parts.Count == 0) return null;
        LoadPartImages(item);
        return item;
    }

    public static PartItem PartFromJson(JsonElement part)
    {
        var item = new PartItem
        {
            Id = part.GetStringProperty("id"),
            MessageId = part.GetStringProperty("messageID"),
            CallId = part.GetStringProperty("callID"),
            Type = part.GetStringProperty("type"),
        };

        if (item.Type is "text" or "reasoning" && part.TryGetProperty("text", out var text))
            item.Text = text.GetString() ?? "";

        item.Synthetic = part.GetBoolProperty("synthetic", false);

        if (item.Type == "reasoning" && part.TryGetProperty("time", out var time))
            item.Time = ParsePartTime(time);

        if (item.Type == "tool")
        {
            item.ToolName = part.GetStringProperty("tool");
            ApplyToolState(item, part);
        }

        if (item.Type == "file")
        {
            item.Mime = part.GetStringProperty("mime");
            item.Url = part.GetStringProperty("url");
            item.FileName = part.GetStringProperty("filename") != "" ? part.GetStringProperty("filename") : item.Url;
        }

        if (part.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
            item.Files = files.EnumerateArray().Select(f => f.GetString() ?? "").Where(f => f.Length > 0).ToArray();

        return item;
    }

    public static ReasoningTime ParsePartTime(JsonElement time)
    {
        return new ReasoningTime
        {
            Start = time.GetInt64Property("start"),
            End = time.GetInt64Property("end"),
        };
    }

    public static void ApplyMessageStats(MessageItem item, JsonElement info)
    {
        item.ModelId = info.GetStringProperty("modelID");
        item.ProviderId = info.GetStringProperty("providerID");
        if (info.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Number)
            item.Cost = cost.GetDouble();
        if (info.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object)
        {
            item.TokensInput = tokens.GetInt64Property("input");
            item.TokensOutput = tokens.GetInt64Property("output");
            item.TokensReasoning = tokens.GetInt64Property("reasoning");
            if (tokens.TryGetProperty("cache", out var cache) && cache.ValueKind == JsonValueKind.Object)
            {
                item.TokensCacheRead = cache.GetInt64Property("read");
                item.TokensCacheWrite = cache.GetInt64Property("write");
            }
        }
    }

    /// <summary>
    /// Classifies how an assistant message's turn ended. Mirrors the opencode web client's
    /// turn-outcome logic (rows.ts interrupted/error detection).
    /// </summary>
    public static ChatOutcome ClassifyMessageOutcome(JsonElement info)
    {
        if (!info.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
            return ChatOutcome.Success;
        return error.GetStringProperty("name") == "MessageAbortedError" ? ChatOutcome.Interrupted : ChatOutcome.Error;
    }

    public static bool IsAbortedError(JsonElement info) => ClassifyMessageOutcome(info) is ChatOutcome.Error;



    /// <summary>
    /// Adds a reactive "error" part when the message carries a non-abort error (e.g. a
    /// streaming failure like <c>"Streaming response failed: [503] The request queue is full."</c>).
    /// Aborts are rendered via the interrupted path instead.
    /// </summary>
    public static void ApplyMessageError(MessageItem message, JsonElement info)
    {
        if (!info.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object) return;
        var name = error.GetStringProperty("name");
        if (name == "MessageAbortedError") return;
        if (message.Parts.Any(p => p.Type == "error")) return;

        message.Parts.Add(new PartItem
        {
            Id = $"error-{message.Id}-{Guid.NewGuid():N}",
            MessageId = message.Id,
            Type = "error",
            ErrorName = name,
            ErrorMessage = UnwrapErrorMessage(error),
        });
    }

    public static string UnwrapErrorMessage(JsonElement error)
    {
        string message = "";
        if (error.TryGetProperty("data", out var data))
        {
            if (data.ValueKind == JsonValueKind.String)
                message = data.GetString() ?? "";
            else if (data.ValueKind == JsonValueKind.Object)
                message = data.GetStringProperty("message");
        }

        message = message.Trim();
        if (message.Length >= 2 && message[0] == '"' && message[^1] == '"')
            message = message[1..^1];
        return message;
    }


    public static void ApplyToolState(PartItem item, JsonElement part)
    {
        if (part.TryGetProperty("state", out var state))
        {
            item.ToolStatus = state.GetStringProperty("status");
            var title = state.GetStringProperty("title");
            if (title.Length > 0) item.ToolTitle = title;            if (state.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object)
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
            if (state.TryGetProperty("output", out var output))
                item.ToolOutput = output.GetString() ?? "";
            if (state.TryGetProperty("error", out var error))
                item.ToolError = error.GetString() ?? "";
            if (state.TryGetProperty("metadata", out var meta) && meta.ValueKind == JsonValueKind.Object)
            {
                if (meta.TryGetProperty("interrupted", out var interm)) item.Interrupted = interm.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.String => interm.GetString() == "true",
                    _ => item.Interrupted,
                };
                if (meta.TryGetProperty("output", out var mOutput)) item.ShellOutput = mOutput.GetString() ?? "";
                if (meta.TryGetProperty("diff", out var mDiff)) item.Diff = mDiff.GetString() ?? "";
                if (meta.TryGetProperty("count", out var mCount)) item.MatchCount = mCount.ToString();
                if (meta.TryGetProperty("matches", out var mMatches)) item.MatchCount = mMatches.ToString();
                if (meta.TryGetProperty("loaded", out var mLoaded) && mLoaded.ValueKind == JsonValueKind.Array)
                    item.LoadedFiles = string.Join("\n", mLoaded.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0));
                if (meta.TryGetProperty("todos", out var mTodos) && mTodos.ValueKind == JsonValueKind.Array)
                    item.TodoJson = JsonSerializer.Serialize(mTodos, AppJsonContext.Default.JsonElement);
                if (meta.TryGetProperty("answers", out var mAnswers) && mAnswers.ValueKind == JsonValueKind.Array)
                    item.AnswerJson = JsonSerializer.Serialize(mAnswers, AppJsonContext.Default.JsonElement);
                // apply_patch records a per-file list here (see apply_patch.ts metadata):
                // { filePath, relativePath, type, patch, additions, deletions, movePath }.
                if (meta.TryGetProperty("files", out var mFiles) && mFiles.ValueKind == JsonValueKind.Array)
                    item.PatchJson = JsonSerializer.Serialize(mFiles, AppJsonContext.Default.JsonElement);
                // The task tool records the spawned subagent session here (see task.ts metadata).
                if (meta.TryGetProperty("sessionId", out var mSession)) item.ToolSessionId = mSession.GetString() ?? "";
                if (meta.TryGetProperty("parentSessionId", out var mParent)) item.ToolParentSessionId = mParent.GetString() ?? "";
            }
        }
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

    /// <summary>Kicks off async decode of image file parts so message thumbnails render.</summary>
    public static void LoadPartImages(MessageItem item)
    {
        foreach (var part in item.Parts)
        {
            if (part.Type == "file") _ = part.LoadImageAsync();
        }
    }
}