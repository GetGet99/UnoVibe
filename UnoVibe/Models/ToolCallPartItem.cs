using System.Text.Json;
using QuickMarkup.Infra.Collections;

namespace UnoVibe.Models;

[QuickRefs("""
    public string? ToolName;
    public string? ToolStatus;
    public string? ToolTitle;
    public string? QuestionRequestId;
    public string? ToolCommand;
    public string? ToolFilePath;
    public string? ToolContent;
    public string? ToolPattern;
    public string? ToolSearchPath;
    public string? ToolInclude;
    public string? ToolWorkdir;
    public string? ToolUrl;
    public string? ToolQuery;
    public string? ToolSkillName;
    public string? ToolSubagentType;
    public string? ToolSessionId;
    public string? ToolParentSessionId;
    public string? ShellOutput;
    public string? Diff;
    public string? MatchCount;
    public string? LoadedFiles;
    public string? ToolInput;
    public string? ToolOutput;
    public string? ToolError;
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
                "bash" or "shell" => ToolCommand is { Length: > 0 } command ? "$ " + command : TitleOrDisplay("Running command..."),
                "glob" => ToolPattern is { Length: > 0 } pattern ? "Glob \"" + pattern + "\"" : TitleOrDisplay("Globbing..."),
                "grep" => BuildGrepTitle(),
                "read" => ToolFilePath is { Length: > 0 } path ? "→ Read " + path : TitleOrDisplay("Reading"),
                "edit" => BuildEditTitle(),
                "write" => BuildWriteTitle(),
                "apply_patch" => BuildPatchTitle(),
                "webfetch" => "% " + (ToolUrl is { Length: > 0 } url ? "WebFetch " + url : TitleOrDisplay("Fetching")),
                "websearch" => "🔍 " + (ToolQuery is { Length: > 0 } query ? "WebSearch " + query : TitleOrDisplay("Searching")),
                "skill" => "→ " + (ToolSkillName is { Length: > 0 } skillName ? "Skill \"" + skillName + "\"" : TitleOrDisplay("Reading skill")),
                "task" => "✳ " + TitleOrDisplay("Delegating..."),
                "todowrite" => TitleOrDisplay("Writing todos..."),
                "question" => TitleOrDisplay("Asking question..."),
                _ => "⚙ " + TitleOrDisplay("Running tool..."),
            };
        }
    }

    public string StatusText
    {
        get
        {
            if (ToolName != "task") return "";
            var type = ToolSubagentType is { Length: > 0 } subagentType ? subagentType : "subagent";
            return ToolStatus switch
            {
                "pending" => $"Starting {type} agent…",
                "running" => $"Running {type} agent…",
                "completed" => ToolSessionId is { Length: > 0 } ? "Done — click to open the session" : "Done",
                "error" => ToolError is { Length: > 0 } toolError ? $"Failed: {toolError}" : "Failed",
                _ => "Click to open the session",
            };
        }
    }

    public string ErrorText
    {
        get
        {
            var error = ToolError;
            if (string.IsNullOrEmpty(error)) return "Question dismissed";
            if (error.StartsWith("Tool execution failed: ", StringComparison.Ordinal))
                error = error.Substring("Tool execution failed: ".Length);
            return error.Length > 0 ? error : "Question dismissed";
        }
    }

    public string LoadedFilesText
    {
        get
        {
            if (string.IsNullOrEmpty(LoadedFiles)) return "";
            return string.Join("\n", LoadedFiles.Split('\n').Select(l => "↳ Loaded " + l));
        }
    }

    public ReactiveList<string> Files { get; } = new();

    public ReactiveList<Integration.Events.TodoInfo> Todos { get; } = new();
    public ReactiveList<List<string>> Answers { get; } = new();
    public ReactiveList<Integration.Events.ApplyPatchFileMeta> PatchFiles { get; } = new();

    public ReactiveList<Integration.QuestionInfo> Questions { get; } = new();
    public ReactiveList<QuestionFormItem> QuestionForm { get; } = new();

    public static string? ToolDisplayName(string? toolName)
    {
        if (string.IsNullOrEmpty(toolName)) return null;
        return ToolDisplayNames.TryGetValue(toolName, out var label) ? label : toolName;
    }

    public string TitleOrDisplay(string fallback) =>
        ToolTitle is { Length: > 0 } title ? title : ToolDisplayName(ToolName) ?? fallback;

    private string BuildGrepTitle()
    {
        var name = ToolPattern is { Length: > 0 } pattern ? "Grep \"" + pattern + "\"" : TitleOrDisplay("Grepping...");
        if (ToolSearchPath is { Length: > 0 } searchPath) name += " in " + searchPath;
        if (ToolInclude is { Length: > 0 } include) name += " (" + include + ")";
        var count = MatchCount is { Length: > 0 } matchCount ? " (" + matchCount + " match" + (matchCount == "1" ? "" : "es") + ")" : "";
        return "✱ " + name + count;
    }

    private string BuildEditTitle()
    {
        var title = ToolFilePath is { Length: > 0 } path ? "← Edit " + path : TitleOrDisplay("Editing");
        var (added, removed) = DiffStats(Diff);
        if (added + removed == 0) return title;
        return $"{title}  ({added}+ {removed}-)";
    }

    private string BuildWriteTitle()
    {
        var title = ToolFilePath is { Length: > 0 } path ? "← Write " + path : TitleOrDisplay("Writing");
        var source = ToolContent is { Length: > 0 } content ? content : ToolOutput is { Length: > 0 } output ? output : ToolInput;
        var lineCount = CountLines(source);
        return lineCount > 0 ? $"{title}  ({lineCount} lines)" : title;
    }

    private string BuildPatchTitle()
    {
        if (PatchFiles.Count == 1) return "← Patch " + PatchFiles[0].RelativePath;
        if (PatchFiles.Count > 1) return $"← Patch {PatchFiles.Count} files";
        if (IsBusy) return "Preparing patch...";
        var title = TitleOrDisplay("Patch").Split('\n')[0].Trim();
        return title.Length > 0 ? title : "Patch";
    }

    public static (int Added, int Removed) DiffStats(string? diff)
    {
        if (string.IsNullOrEmpty(diff)) return (0, 0);
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

    public static int CountLines(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
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
    public Integration.Events.ToolMetadata? Metadata { get; set; }
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
