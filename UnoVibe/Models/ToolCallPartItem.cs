using System.Text.Json;
using QuickMarkup.Infra.Collections;

namespace UnoVibe.Models;

[QuickRefs("""
    public string ToolName = "";
    public string ToolStatus = "";
    public string ToolTitle = "";
    public string QuestionRequestId = "";
    // Every other field read in markup must be reactive too: since parts are updated
    // in place (same instance, see MessageJsonHelper.ApplyToolPart), a plain property
    // assignment would never re-render the views bound to it (e.g. shell output).
    public string ToolCommand = "";
    public string ToolFilePath = "";
    public string ToolContent = "";
    public string ToolPattern = "";
    public string ToolSearchPath = "";
    public string ToolInclude = "";
    public string ToolWorkdir = "";
    public string ToolUrl = "";
    public string ToolQuery = "";
    public string ToolSkillName = "";
    public string ToolSubagentType = "";
    public string ToolSessionId = "";
    public string ToolParentSessionId = "";
    public string ShellOutput = "";
    public string Diff = "";
    public string MatchCount = "";
    public string LoadedFiles = "";
    public string ToolInput = "";
    public string ToolOutput = "";
    public string ToolError = "";
    public bool Interrupted = false;
    """)]
partial class ToolCallPartItem : ChatPartItem
{
    public override string Type => "tool";
    public string CallId { get; set; } = "";
    public ToolCallState State { get; set; } = null!;

    public bool IsBusy => ToolStatus is "pending" or "running";

    public string DisplayName
    {
        get
        {
            return ToolName switch
            {
                "bash" or "shell" => ToolCommand.Length > 0 ? "$ " + ToolCommand : ToolTitle ?? ToolDisplayName() ?? "Running command...",
                "glob" => ToolPattern.Length > 0 ? "Glob \"" + ToolPattern + "\"" : ToolTitle ?? ToolDisplayName() ?? "Globbing...",
                "grep" => BuildGrepTitle(),
                "read" => ToolFilePath.Length > 0 ? "→ Read " + ToolFilePath : ToolTitle ?? ToolDisplayName() ?? "Reading",
                "edit" => BuildEditTitle(),
                "write" => BuildWriteTitle(),
                "apply_patch" => BuildPatchTitle(),
                "webfetch" => "% " + (ToolUrl.Length > 0 ? "WebFetch " + ToolUrl : ToolTitle ?? ToolDisplayName() ?? "Fetching"),
                "websearch" => "🔍 " + (ToolQuery.Length > 0 ? "WebSearch " + ToolQuery : ToolTitle ?? ToolDisplayName() ?? "Searching"),
                "skill" => "→ " + (ToolSkillName.Length > 0 ? "Skill \"" + ToolSkillName + "\"" : ToolTitle ?? ToolDisplayName() ?? "Reading skill"),
                "task" => "✳ " + (ToolTitle?.Length > 0 ? ToolTitle : ToolDisplayName() ?? "Delegating..."),
                "todowrite" => ToolTitle?.Length > 0 ? ToolTitle : ToolDisplayName() ?? "Writing todos...",
                "question" => ToolTitle?.Length > 0 ? ToolTitle : ToolDisplayName() ?? "Asking question...",
                _ => "⚙ " + (ToolTitle ?? ToolDisplayName() ?? "Running tool..."),
            };
        }
    }

    public string StatusText
    {
        get
        {
            if (ToolName != "task") return "";
            var type = ToolSubagentType.Length > 0 ? ToolSubagentType : "subagent";
            return ToolStatus switch
            {
                "pending" => $"Starting {type} agent…",
                "running" => $"Running {type} agent…",
                "completed" => ToolSessionId.Length > 0 ? "Done — click to open the session" : "Done",
                "error" => ToolError.Length > 0 ? $"Failed: {ToolError}" : "Failed",
                _ => "Click to open the session",
            };
        }
    }

    public string ErrorText
    {
        get
        {
            var error = ToolError;
            if (error.StartsWith("Tool execution failed: ", StringComparison.Ordinal))
                error = error.Substring("Tool execution failed: ".Length);
            return error.Length > 0 ? error : "Question dismissed";
        }
    }

    public string LoadedFilesText
    {
        get
        {
            if (LoadedFiles.Length == 0) return "";
            return string.Join("\n", LoadedFiles.Split('\n').Select(l => "↳ Loaded " + l));
        }
    }

    // Tool-specific input fields and metadata-derived fields live in the [QuickRefs] block
    // above (they must be reactive — parts are updated in place). Only non-bound state stays here.

    // Attachment URLs extracted from completed state
    public ReactiveList<string> Files { get; } = new();

    // Typed metadata collections (populated from ToolMetadata by MessageJsonHelper).
    // ReactiveLists that are never reassigned, only refilled in place, so live views update
    // without swapping the part instance — see MessageJsonHelper.ApplyToolPart.
    public ReactiveList<Integration.Events.TodoInfo> Todos { get; } = new();
    public ReactiveList<List<string>> Answers { get; } = new();
    public ReactiveList<Integration.Events.ApplyPatchFileMeta> PatchFiles { get; } = new();

    // Question support
    public ReactiveList<Integration.QuestionInfo> Questions { get; } = new();
    public ReactiveList<QuestionFormItem> QuestionForm { get; } = new();

    // TODO: Add GetInput<T>() back when typed tool input classes are implemented (Phase 2c)

    private string? ToolDisplayName()
    {
        if (string.IsNullOrEmpty(ToolName)) return null;
        return ToolDisplayNames.TryGetValue(ToolName, out var label) ? label : ToolName;
    }

    private string BuildGrepTitle()
    {
        var name = ToolPattern.Length > 0 ? "Grep \"" + ToolPattern + "\"" : ToolTitle ?? ToolDisplayName() ?? "Grepping...";
        if (ToolSearchPath.Length > 0) name += " in " + ToolSearchPath;
        if (ToolInclude.Length > 0) name += " (" + ToolInclude + ")";
        var count = MatchCount.Length > 0 ? " (" + MatchCount + " match" + (MatchCount == "1" ? "" : "es") + ")" : "";
        return "✱ " + name + count;
    }

    private string BuildEditTitle()
    {
        var title = ToolFilePath.Length > 0 ? "← Edit " + ToolFilePath : ToolTitle ?? ToolDisplayName() ?? "Editing";
        var (added, removed) = DiffStats(Diff);
        if (added + removed == 0) return title;
        return $"{title}  ({added}+ {removed}-)";
    }

    private string BuildWriteTitle()
    {
        var title = ToolFilePath.Length > 0 ? "← Write " + ToolFilePath : ToolTitle ?? ToolDisplayName() ?? "Writing";
        var lineCount = CountLines(ToolContent.Length > 0 ? ToolContent : ToolOutput.Length > 0 ? ToolOutput : ToolInput);
        return lineCount > 0 ? $"{title}  ({lineCount} lines)" : title;
    }

    private string BuildPatchTitle()
    {
        if (PatchFiles.Count == 1) return "← Patch " + PatchFiles[0].RelativePath;
        if (PatchFiles.Count > 1) return $"← Patch {PatchFiles.Count} files";
        if (IsBusy) return "Preparing patch...";
        var title = (ToolTitle ?? ToolDisplayName() ?? "Patch").Split('\n')[0].Trim();
        return title.Length > 0 ? title : "Patch";
    }

    public static (int Added, int Removed) DiffStats(string diff)
    {
        int added = 0, removed = 0;
        foreach (var line in diff.Split('\n'))
        {
            if (line.Length < 1 || line[0] != '+' && line[0] != '-') continue;
            if (line.StartsWith("+++") || line.StartsWith("---")) continue;
            if (line[0] == '+') added++;
            else removed++;
        }
        return (added, removed);
    }

    public static int CountLines(string value)
    {
        if (value.Length == 0) return 0;
        var count = 1;
        foreach (var c in value)
            if (c == '\n') count++;
        return value[value.Length - 1] == '\n' ? count - 1 : count;
    }

    private static readonly IReadOnlyDictionary<string, string> ToolDisplayNames =
        new Dictionary<string, string>
        {
            ["bash"] = "Running command...",
            ["shell"] = "Running command...",
            ["glob"] = "Globbing...",
            ["grep"] = "Grepping...",
            ["webfetch"] = "Fetching",
            ["websearch"] = "Searching",
            ["skill"] = "Reading skill",
            ["read"] = "Reading",
            ["edit"] = "Editing",
            ["write"] = "Writing",
            ["apply_patch"] = "Preparing patch...",
            ["todowrite"] = "Writing todos...",
            ["question"] = "Asking question...",
            ["task"] = "Delegating...",
        };
}

#region Tool call state hierarchy

public abstract partial class ToolCallState
{
    public JsonElement Input { get; set; }
    public abstract string Status { get; }
}

partial class ToolPendingState : ToolCallState
{
    public override string Status => "pending";
    public string Raw { get; set; } = "";
}

partial class ToolRunningState : ToolCallState
{
    public override string Status => "running";
    public string? Title { get; set; }
    public JsonElement? Structured { get; set; }
    public List<ToolContentItem>? Content { get; set; }
}

partial class ToolCompletedState : ToolCallState
{
    public override string Status => "completed";
    public string Title { get; set; } = "";
    public string Output { get; set; } = "";
    public JsonElement? Structured { get; set; }
    public List<ToolContentItem>? Content { get; set; }
    public List<string>? OutputPaths { get; set; }
    public JsonElement? Result { get; set; }
    public List<FileAttachmentInfo>? Attachments { get; set; }
    public Integration.Events.ToolMetadata? Metadata { get; set; }
}

partial class ToolErrorState : ToolCallState
{
    public override string Status => "error";
    public string Error { get; set; } = "";
    public JsonElement? Structured { get; set; }
    public List<ToolContentItem>? Content { get; set; }
    public JsonElement? Result { get; set; }
    public Integration.Events.ToolMetadata? Metadata { get; set; }
}

#endregion

#region Supporting types for tool content

sealed class ToolContentItem
{
    public string Type { get; set; } = "";
    public string Text { get; set; } = "";
    public string Uri { get; set; } = "";
    public string Mime { get; set; } = "";
    public string? Name { get; set; }
}

sealed class FileAttachmentInfo
{
    public string Url { get; set; } = "";
    public string Mime { get; set; } = "";
    public string? Name { get; set; }
}

#endregion
