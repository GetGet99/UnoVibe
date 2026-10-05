using System.Text.Json;
using UnoVibe.Integration.Events;

namespace UnoVibe.Controls.ToolViews;

static class ToolViewShared
{
    public static string FormatDuration(long ms)
    {
        if (ms <= 0) return "";
        if (ms < 1000) return $"{ms}ms";
        if (ms < 60000) return $"{(ms / 1000.0):0.#}s";
        if (ms < 3600000)
        {
            var minutes = ms / 60000;
            var seconds = (ms % 60000) / 1000;
            return seconds > 0 ? $"{minutes}m {seconds}s" : $"{minutes}m";
        }
        if (ms < 86400000)
        {
            var hours = ms / 3600000;
            var minutes = (ms % 3600000) / 60000;
            return minutes > 0 ? $"{hours}h {minutes}m" : $"{hours}h";
        }
        var days = ms / 86400000;
        var h = (ms % 86400000) / 3600000;
        return $"{days}d {h}h";
    }

    public static bool Busy(ToolCallPartItem p) => p.IsBusy;

    public static string Shell(ToolCallPartItem p) =>
        p.ToolCommand is { Length: > 0 } command
            ? "$ " + command
            : p.TitleOrDisplay("Running command...");

    public static string ShellWorkdir(ToolCallPartItem p, string? referenceDir)
    {
        var workdir = p.ToolWorkdir;
        if (string.IsNullOrEmpty(workdir) || string.IsNullOrEmpty(referenceDir)) return workdir ?? "";
        try
        {
            var full = Path.IsPathRooted(workdir)
                ? Path.GetFullPath(workdir)
                : Path.GetFullPath(Path.Combine(referenceDir, workdir));
            var relative = Path.GetRelativePath(Path.GetFullPath(referenceDir), full);
            if (relative.Length == 0 || relative == ".") return "";
            if (relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar))
                return relative;
        }
        catch { }
        return workdir;
    }

    public static string Glob(ToolCallPartItem p)
    {
        var name = p.ToolPattern is { Length: > 0 } pattern ? "Glob \"" + pattern + "\"" : p.TitleOrDisplay("Globbing...");
        var count = p.MatchCount is { Length: > 0 } matchCount ? " (" + matchCount + " match" + (matchCount == "1" ? "" : "es") + ")" : "";
        return "✱ " + name + count;
    }

    public static string Grep(ToolCallPartItem p)
    {
        var name = p.ToolPattern is { Length: > 0 } pattern ? "Grep \"" + pattern + "\"" : p.TitleOrDisplay("Grepping...");
        if (p.ToolSearchPath is { Length: > 0 } searchPath) name += " in " + searchPath;
        if (p.ToolInclude is { Length: > 0 } include) name += " (" + include + ")";
        var count = p.MatchCount is { Length: > 0 } matchCount ? " (" + matchCount + " match" + (matchCount == "1" ? "" : "es") + ")" : "";
        return "✱ " + name + count;
    }

    public static string TodoTitle(ToolCallPartItem p) =>
        p.TitleOrDisplay("Writing todos...");

    public static string TodoLine(TodoInfo todo)
    {
        var mark = todo.Status switch
        {
            "completed" => "[✓]",
            "in_progress" => "[•]",
            _ => "[ ]",
        };
        return mark + " " + todo.Content;
    }

    public static string QuestionTitle(ToolCallPartItem p) =>
        p.TitleOrDisplay("Asking question...");

    public static string QuestionError(ToolCallPartItem p)
    {
        var error = p.ToolError;
        if (string.IsNullOrEmpty(error)) return "Question dismissed";
        if (error.StartsWith("Tool execution failed: ", StringComparison.Ordinal))
            error = error.Substring("Tool execution failed: ".Length);
        return error.Length > 0 ? error : "Question dismissed";
    }

    public static List<QuestionItem> ParseQuestions(ToolCallPartItem p) => ParseQuestions(p.Questions, p.Answers);

    public static List<QuestionItem> ParseQuestions(IReadOnlyList<Integration.QuestionInfo> questionsInfo, IReadOnlyList<List<string>> answers)
    {
        var list = new List<QuestionItem>();
        if (questionsInfo.Count == 0) return list;
        try
        {
            var i = 0;
            foreach (var qInfo in questionsInfo)
            {
                var q = new QuestionItem
                {
                    Question = qInfo.Question,
                    Header = qInfo.Header,
                    Answer = i < answers.Count ? string.Join(", ", answers[i]) : "",
                };
                if (q.Question.Length > 0) list.Add(q);
                i++;
            }
        }
        catch (JsonException) { }
        return list;
    }

    public static string WebFetch(ToolCallPartItem p) =>
        "% " + (p.ToolUrl is { Length: > 0 } url ? "WebFetch " + url : p.TitleOrDisplay("Fetching"));

    public static string Skill(ToolCallPartItem p) =>
        "→ " + (p.ToolSkillName is { Length: > 0 } skillName ? "Skill \"" + skillName + "\"" : p.TitleOrDisplay("Reading skill"));

    public static string Read(ToolCallPartItem p) =>
        "→ " + (p.ToolFilePath is { Length: > 0 } path ? "Read " + path : p.TitleOrDisplay("Reading"));

    public static string Loaded(ToolCallPartItem p)
    {
        if (string.IsNullOrEmpty(p.LoadedFiles)) return "";
        return string.Join("\n", p.LoadedFiles.Split('\n').Select(l => "↳ Loaded " + l));
    }

    public static string Edit(ToolCallPartItem p) =>
        "← " + (p.ToolFilePath is { Length: > 0 } path ? "Edit " + path : p.TitleOrDisplay("Editing"));

    public static string Write(ToolCallPartItem p) =>
        "← " + (p.ToolFilePath is { Length: > 0 } path ? "Write " + path : p.TitleOrDisplay("Writing"));

    public static string EditTitle(ToolCallPartItem p)
    {
        var (added, removed) = DiffStats(p.Diff);
        if (added + removed == 0) return Edit(p);
        return $"{Edit(p)}  ({added}+ {removed}-)";
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

    public static string WriteTitle(ToolCallPartItem p)
    {
        var title = Write(p);
        var source = p.ToolContent is { Length: > 0 } content ? content : p.ToolOutput is { Length: > 0 } output ? output : p.ToolInput;
        var lineCount = CountLines(source);
        return lineCount > 0 ? $"{title}  ({lineCount} lines)" : title;
    }

    public static string Patch(ToolCallPartItem p)
    {
        if (p.PatchFiles.Count == 1) return "← Patch " + p.PatchFiles[0].RelativePath;
        if (p.PatchFiles.Count > 1) return $"← Patch {p.PatchFiles.Count} files";
        if (Busy(p)) return "Preparing patch...";
        var title = p.TitleOrDisplay("Patch").Split('\n')[0].Trim();
        return title.Length > 0 ? title : "Patch";
    }

    public static string PatchFileLine(Integration.Events.ApplyPatchFileMeta f)
    {
        var label = f.Type switch
        {
            "add" => "# Created ",
            "delete" => "# Deleted ",
            "move" => "# Moved " + (f.FilePath.Length > 0 ? f.FilePath + " → " : ""),
            _ => "← Patched ",
        };
        var text = label + f.RelativePath;
        if (f.Additions + f.Deletions > 0)
            text += $"  ({f.Additions}+ {f.Deletions}-)";
        return text;
    }

    public static int CountLines(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        var count = 1;
        foreach (var c in value)
            if (c == '\n') count++;
        return value[value.Length - 1] == '\n' ? count - 1 : count;
    }

    public static string Generic(ToolCallPartItem p) =>
        "⚙ " + p.TitleOrDisplay("Running tool...");

    public static string Task(ToolCallPartItem p)
    {
        var name = p.TitleOrDisplay("Delegating...");
        return "✳ " + name;
    }

    public static string TaskStatus(ToolCallPartItem p)
    {
        var type = p.ToolSubagentType is { Length: > 0 } subagentType ? subagentType : "subagent";
        return p.ToolStatus switch
        {
            "pending" => $"Starting {type} agent…",
            "running" => $"Running {type} agent…",
            "completed" => p.ToolSessionId is { Length: > 0 } ? "Done — click to open the session" : "Done",
            "error" => p.ToolError is { Length: > 0 } toolError ? $"Failed: {toolError}" : "Failed",
            _ => "Click to open the session",
        };
    }

    public static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Length <= max) return value;
        return value.Substring(0, max) + "\n… (truncated, " + (value.Length - max) + " more chars)";
    }

    public const int ShellMaxLines = 10;
    public const int ShellMaxChars = ShellMaxLines * 120;

    public static bool ShellOverflow(ToolCallPartItem p) => CollapseShellOutput(p).Overflow;

    public static string ShellCollapsed(ToolCallPartItem p) => CollapseShellOutput(p).Output;

    private static (string Output, bool Overflow) CollapseShellOutput(ToolCallPartItem p)
    {
        var output = p.ShellOutput is { Length: > 0 } shellOutput ? shellOutput : p.ToolOutput;
        if (string.IsNullOrEmpty(output)) return ("", false);
        return CollapseLines(output, ShellMaxLines, ShellMaxChars);
    }

    public static bool GenericInputOverflow(ToolCallPartItem p) => GenericCollapse(p.ToolInput).Overflow;

    public static string GenericInputCollapsed(ToolCallPartItem p) => GenericCollapse(p.ToolInput).Output;

    public static bool GenericOutputOverflow(ToolCallPartItem p) => GenericCollapse(p.ToolOutput).Overflow;

    public static string GenericOutputCollapsed(ToolCallPartItem p) => GenericCollapse(p.ToolOutput).Output;

    private static (string Output, bool Overflow) GenericCollapse(string? value)
    {
        if (string.IsNullOrEmpty(value)) return ("", false);
        return CollapseLines(value, ShellMaxLines, ShellMaxChars);
    }

    public static (string Output, bool Overflow) CollapseLines(string output, int maxLines, int maxChars)
    {
        var lines = output.Split('\n');
        if (lines.Length <= maxLines && output.Length <= maxChars)
            return (output, false);

        var preview = string.Join("\n", lines.Take(maxLines));
        if (preview.Length > maxChars)
            return (string.Concat(preview.AsSpan(0, Math.Max(0, maxChars - 1)), "…"), true);

        return (preview + "\n…", true);
    }

    public static (string Preview, bool Overflow) CollapsePreview(string output, int maxLines, int maxChars)
    {
        var lines = output.Split('\n');
        if (lines.Length <= maxLines && output.Length <= maxChars)
            return (output, false);

        var preview = string.Join("\n", lines.Take(maxLines));
        if (preview.Length > maxChars)
            preview = preview.Substring(0, Math.Max(0, maxChars - 1));
        return (preview, true);
    }

    private static string GetString(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var prop)
            ? prop.GetString() ?? ""
            : "";

    private static int GetInt(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var prop)
            ? prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value) ? value : 0
            : 0;
}
