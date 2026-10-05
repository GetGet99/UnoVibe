using System.Text.Json;
using QuickMarkup.Infra.Collections;
using UnoVibe.Integration.Events;

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
        ApplyMessageTime(item, info);
        if (parts is not null)
        {
            foreach (var part in parts)
            {
                if (part is StepStartPart or StepFinishPart) continue;
                var p = PartFromPart(part);
                if (p.Type == "text" && p is TextPartItem text && text.Synthetic) continue;
                if (p.Id.Length > 0) item.Parts.Add(p);
            }
        }
        if (info is AssistantMessageInfo assist && IsAbortedError(assist) && item.Parts.All(p => p.Type != "aborted"))
        {
            item.Interrupted = true;
            item.Parts.Add(new AbortedPartItem { Id = $"aborted-{item.Id}", MessageId = item.Id });
        }
        else if (info is AssistantMessageInfo assist2)
        {
            ApplyMessageError(item, assist2);
        }
        if (item.Role == "user" && item.Parts.Count == 0) return null;
        LoadPartImages(item);
        return item;
    }

    public static ChatPartItem PartFromPart(Part part)
    {
        return part switch
        {
            TextPart text => new TextPartItem
            {
                Id = part.Id,
                MessageId = part.MessageId,
                Text = text.Text,
                Synthetic = text.Synthetic ?? false,
            },
            ReasoningPart reasoning => new ReasoningPartItem
            {
                Id = part.Id,
                MessageId = part.MessageId,
                Text = reasoning.Text,
                Time = new ReasoningTime
                {
                    Start = (long)reasoning.Time.Start,
                    End = reasoning.Time.End.HasValue ? (long)reasoning.Time.End.Value : 0,
                },
            },
            FilePart file => new FilePartItem
            {
                Id = part.Id,
                MessageId = part.MessageId,
                Mime = file.Mime,
                Url = file.Url,
                FileName = file.Filename ?? file.Url,
            },
            ToolPart tool => BuildToolCallPart(part, tool),
            StepStartPart step => new StepStartPartItem
            {
                Id = part.Id,
                MessageId = part.MessageId,
                Snapshot = step.Snapshot,
            },
            StepFinishPart finish => new StepFinishPartItem
            {
                Id = part.Id,
                MessageId = part.MessageId,
                Reason = finish.Reason,
                Snapshot = finish.Snapshot,
                Cost = finish.Cost,
                Tokens = finish.Tokens,
            },
            SnapshotPart snapshot => new SnapshotPartItem
            {
                Id = part.Id,
                MessageId = part.MessageId,
                Snapshot = snapshot.Snapshot,
            },
            PatchPart patch => new PatchPartItem
            {
                Id = part.Id,
                MessageId = part.MessageId,
                Hash = patch.Hash,
                Files = patch.Files,
            },
            AgentPart agent => new AgentPartItem
            {
                Id = part.Id,
                MessageId = part.MessageId,
                Name = agent.Name,
            },
            RetryPart retry => new RetryPartItem
            {
                Id = part.Id,
                MessageId = part.MessageId,
                Attempt = retry.Attempt,
                ErrorMessage = retry.Error switch
                {
                    ProviderAuthError auth => auth.Data.Message,
                    UnknownAssistantError unknown => unknown.Data.Message,
                    AbortedError aborted => aborted.Data.Message,
                    StructuredOutputError structured => structured.Data.Message,
                    ContextOverflowError context => context.Data.Message,
                    ContentFilterError content => content.Data.Message,
                    ApiAssistantError api => api.Data.Message,
                    _ => "",
                },
                Time = new ReasoningTime
                {
                    Start = (long)retry.Time.Created,
                },
            },
            CompactionPart compaction => new CompactionPartItem
            {
                Id = part.Id,
                MessageId = part.MessageId,
                Auto = compaction.Auto,
                Overflow = compaction.Overflow,
            },
            SubtaskPart subtask => new SubtaskPartItem
            {
                Id = part.Id,
                MessageId = part.MessageId,
                Prompt = subtask.Prompt,
                Description = subtask.Description,
                Agent = subtask.Agent,
                ModelProviderId = subtask.Model?.ProviderId,
                ModelModelId = subtask.Model?.ModelId,
                Command = subtask.Command,
            },
            _ => new TextPartItem
            {
                Id = part.Id,
                MessageId = part.MessageId,
                Text = "",
            },
        };
    }

    private static ToolCallPartItem BuildToolCallPart(Part part, ToolPart tool)
    {
        var item = new ToolCallPartItem
        {
            Id = part.Id,
            MessageId = part.MessageId,
            CallId = tool.CallId,
            ToolName = tool.Tool,
        };

        ApplyToolPart(item, tool);
        PopulateQuestionForm(item, item.Questions);
        return item;
    }

    public static void ApplyToolPart(ToolCallPartItem item, ToolPart tool)
    {
        ToolCallState state = tool.State switch
        {
            ToolStatePending pending => new ToolPendingState
            {
                Input = pending.Input,
                Raw = pending.Raw,
            },
            ToolStateRunning running => new ToolRunningState
            {
                Input = running.Input,
                Title = running.Title,
                Metadata = running.Metadata,
            },
            ToolStateCompleted completed => new ToolCompletedState
            {
                Input = completed.Input,
                Title = completed.Title,
                Output = completed.Output,
                Metadata = completed.Metadata,
                Attachments = completed.Attachments?.Select(a => new FileAttachmentInfo
                {
                    Url = a.Url,
                    Mime = a.Mime,
                }).ToList(),
            },
            ToolStateError errorState => new ToolErrorState
            {
                Input = errorState.Input,
                Error = errorState.Error,
                Metadata = errorState.Metadata,
            },
            _ => new ToolPendingState { Input = default },
        };

        item.State = state;
        item.ToolStatus = state.Status;

        if (state is ToolRunningState runningState)
        {
            if (!string.IsNullOrWhiteSpace(runningState.Title))
                item.ToolTitle = runningState.Title;
            if (runningState.Metadata is { } runningMeta)
                ApplyToolMetadata(item, runningMeta);
        }
        if (state is ToolCompletedState completedState)
        {
            item.ToolTitle = NullIfBlank(completedState.Title);
            item.ToolOutput = NullIfBlank(completedState.Output);
            if (completedState.Metadata is { } meta)
                ApplyToolMetadata(item, meta);
            if (completedState.Attachments is { } attachments)
                Refill(item.Files, attachments.Select(a => a.Url).Where(u => u.Length > 0));
        }
        if (state is ToolErrorState errorState2)
        {
            item.ToolError = NullIfBlank(errorState2.Error);
            if (errorState2.Metadata is { } meta)
                ApplyToolMetadata(item, meta);
        }

        ApplyToolInput(item, tool.State.Input);
    }

    public static void Refill<T>(ReactiveList<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var v in values) target.Add(v);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

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

    public static void ApplyMessageTime(MessageItem item, MessageInfo info)
    {
        switch (info)
        {
            case UserMessageInfo user:
                item.CreatedMs = (long)user.Time.Created;
                item.CompletedMs = 0;
                break;
            case AssistantMessageInfo assistant:
                item.CreatedMs = (long)assistant.Time.Created;
                item.CompletedMs = assistant.Time.Completed.HasValue ? (long)assistant.Time.Completed.Value : 0;
                break;
        }
    }

    public static ChatOutcome ClassifyMessageOutcome(AssistantMessageInfo info)
    {
        if (info.Error is not {} error)
            return ChatOutcome.Success;
        return error is AbortedError ? ChatOutcome.Interrupted : ChatOutcome.Error;
    }

    public static bool IsAbortedError(AssistantMessageInfo info) => ClassifyMessageOutcome(info) is ChatOutcome.Interrupted;

    public static void ApplyMessageError(MessageItem message, AssistantMessageInfo info)
    {
        if (info.Error is not { } error) return;
        if (error is AbortedError) return;
        if (message.Parts.Any(p => p.Type == "error")) return;

        string errorMessage = error switch
        {
            ProviderAuthError auth => auth.Data.Message,
            UnknownAssistantError unknown => unknown.Data.Message,
            ApiAssistantError api => api.Data.Message,
            _ => "",
        };

        message.Parts.Add(new ErrorPartItem
        {
            Id = $"error-{message.Id}-{Guid.NewGuid():N}",
            MessageId = message.Id,
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

    private static void ApplyToolInput(ToolCallPartItem item, JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) return;
        var serialized = JsonSerializer.Serialize(input, AppJsonContext.Default.JsonElement);
        if (serialized != "{}") item.ToolInput = serialized;
        if (input.TryGetProperty("command", out var command)) item.ToolCommand = NullIfBlank(command.GetString());
        if (input.TryGetProperty("filePath", out var filePath)) item.ToolFilePath = NullIfBlank(filePath.GetString());
        if (input.TryGetProperty("content", out var content)) item.ToolContent = NullIfBlank(content.GetString());
        if (input.TryGetProperty("pattern", out var pattern)) item.ToolPattern = NullIfBlank(pattern.GetString());
        if (input.TryGetProperty("path", out var searchPath)) item.ToolSearchPath = NullIfBlank(searchPath.GetString());
        if (input.TryGetProperty("include", out var include)) item.ToolInclude = NullIfBlank(include.GetString());
        if (input.TryGetProperty("workdir", out var workdir)) item.ToolWorkdir = NullIfBlank(workdir.GetString());
        if (input.TryGetProperty("url", out var url)) item.ToolUrl = NullIfBlank(url.GetString());
        if (input.TryGetProperty("query", out var query)) item.ToolQuery = NullIfBlank(query.GetString());
        if (input.TryGetProperty("name", out var skillName)) item.ToolSkillName = NullIfBlank(skillName.GetString());
        if (input.TryGetProperty("subagent_type", out var subType)) item.ToolSubagentType = NullIfBlank(subType.GetString());
        if (input.TryGetProperty("todos", out var todos) && todos.ValueKind == JsonValueKind.Array)
            Refill(item.Todos, todos.Deserialize(AppJsonContext.Default.ListTodoInfo) ?? []);
        if (input.TryGetProperty("questions", out var questions) && questions.ValueKind == JsonValueKind.Array)
            Refill(item.Questions, questions.Deserialize(AppJsonContext.Default.ListQuestionInfo) ?? []);
    }

    private static void ApplyToolMetadata(ToolCallPartItem item, ToolMetadata meta)
    {
        if (meta.Interrupted == true) item.Interrupted = true;
        if (!string.IsNullOrWhiteSpace(meta.Output)) item.ShellOutput = meta.Output;
        if (!string.IsNullOrWhiteSpace(meta.Diff)) item.Diff = meta.Diff;
        if (meta.Count is { } count) item.MatchCount = count.ToString();
        if (meta.Matches is { } matches) item.MatchCount = matches.ToString();
        if (meta.Loaded is { Count: > 0 } loaded)
        {
            var joined = string.Join("\n", loaded.Where(s => !string.IsNullOrWhiteSpace(s)));
            item.LoadedFiles = joined.Length > 0 ? joined : null;
        }
        if (meta.Todos is { Count: > 0 } todos)
            Refill(item.Todos, todos);
        if (meta.Answers is { Count: > 0 } answers)
            Refill(item.Answers, answers);
        if (meta.Files is { Count: > 0 } files)
            Refill(item.PatchFiles, files);
        if (!string.IsNullOrWhiteSpace(meta.SessionId)) item.ToolSessionId = meta.SessionId;
        if (!string.IsNullOrWhiteSpace(meta.ParentSessionId)) item.ToolParentSessionId = meta.ParentSessionId;
    }

    public static void PopulateQuestionForm(ToolCallPartItem item, IReadOnlyList<Integration.QuestionInfo> questions)
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
            if (part is FilePartItem file) _ = file.LoadImageAsync();
        }
    }
}
