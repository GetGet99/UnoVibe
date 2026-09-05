using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnoVibe.Models;

namespace UnoVibe.Controls.ToolViews;

public static class ToolViewShared
{
    private static readonly IReadOnlyDictionary<string, string> ToolDisplayNames =
        new Dictionary<string, string>
        {
            ["bash"] = "Running command...",
            ["shell"] = "Running command...",
            ["glob"] = "Globbing...",
            ["grep"] = "Grepping...",
            ["webfetch"] = "Fetching",
            ["skill"] = "Reading skill",
            ["read"] = "Reading",
            ["edit"] = "Editing",
            ["write"] = "Writing",
            ["apply_patch"] = "Preparing patch...",
            ["todowrite"] = "Writing todos...",
            ["question"] = "Asking question...",
            ["task"] = "Delegating...",
        };

    /// <summary>
    /// Maps a raw tool name to its friendly display label (the same text each view's
    /// last-resort fallback uses), so a title-less running tool shows "Editing" instead
    /// of leaking the raw "edit". Unknown names pass through unchanged.
    /// </summary>
    public static string? ToolDisplayName(string? toolName)
    {
        if (string.IsNullOrEmpty(toolName)) return null;
        return ToolDisplayNames.TryGetValue(toolName, out var label) ? label : toolName;
    }

    public static (string Title, string Body) ReasoningSummary(PartItem p)
    {
        var content = p.Text.Replace("[REDACTED]", "").Trim();
        if (content.Length == 0) return ("", "");
        var match = Regex.Match(content, @"^\*\*([^*\n]+)\*\*(?:\r?\n\r?\n|$)");
        if (!match.Success) return ("", content);
        return (match.Groups[1].Value.Trim(), content.Substring(match.Length).Trim());
    }

    public static string ReasoningLabel(PartItem p)
    {
        var (title, _) = ReasoningSummary(p);
        return title.Length > 0 ? "Thinking: " + title : "Thinking";
    }

    public static string ThoughtLabel(PartItem p)
    {
        var (title, _) = ReasoningSummary(p);
        var text = "Thought";
        if (title.Length > 0) text += ": " + title;
        var duration = FormatDuration(p.Time.DurationMs);
        if (duration.Length > 0) text += " · " + duration;
        return text;
    }

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
    /// <summary>
    /// True while a tool part is not finished: either the model is still streaming
    /// the tool-call arguments ("pending") or the tool call is executing ("running").
    /// </summary>
    public static bool Busy(PartItem p) => p.ToolStatus is "pending" or "running";

    public static string Shell(PartItem p) =>
        p.ToolCommand.Length > 0
            ? "$ " + p.ToolCommand
            : p.ToolTitle ?? ToolDisplayName(p.ToolName) ?? "Running command...";

    /// <summary>
    /// Workdir label for a shell tool card: the working directory relative to the session's
    /// directory ("sub/dir"), or "" when there is nothing to show — no workdir in the input,
    /// no known session directory, or the workdir IS the session directory. Paths outside
    /// the session directory fall back to the raw workdir. Mirrors the TUI's
    /// <c>workdirDisplay</c> (relative-to-location, hidden when it resolves to ".").
    /// </summary>
    public static string ShellWorkdir(PartItem p, string referenceDir)
    {
        var workdir = p.ToolWorkdir;
        if (workdir.Length == 0 || referenceDir.Length == 0) return workdir;
        try
        {
            var full = Path.IsPathRooted(workdir)
                ? Path.GetFullPath(workdir)
                : Path.GetFullPath(Path.Combine(referenceDir, workdir));
            var relative = Path.GetRelativePath(Path.GetFullPath(referenceDir), full);
            if (relative.Length == 0 || relative == ".") return ""; // same folder as the session
            if (relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar))
                return relative;
        }
        catch { }
        return workdir;
    }

    public static string Glob(PartItem p)
    {
        var name = p.ToolPattern.Length > 0 ? "Glob \"" + p.ToolPattern + "\"" : p.ToolTitle ?? ToolDisplayName(p.ToolName) ?? "Globbing...";
        var count = p.MatchCount.Length > 0 ? " (" + p.MatchCount + " match" + (p.MatchCount == "1" ? "" : "es") + ")" : "";
        return "✱ " + name + count;
    }

    public static string Grep(PartItem p)
    {
        var name = p.ToolPattern.Length > 0 ? "Grep \"" + p.ToolPattern + "\"" : p.ToolTitle ?? ToolDisplayName(p.ToolName) ?? "Grepping...";
        if (p.ToolSearchPath.Length > 0) name += " in " + p.ToolSearchPath;
        if (p.ToolInclude.Length > 0) name += " (" + p.ToolInclude + ")";
        var count = p.MatchCount.Length > 0 ? " (" + p.MatchCount + " match" + (p.MatchCount == "1" ? "" : "es") + ")" : "";
        return "✱ " + name + count;
    }

    public static string TodoTitle(PartItem p) =>
        p.ToolTitle?.Length > 0 ? p.ToolTitle : ToolDisplayName(p.ToolName) ?? "Writing todos...";

    public static string TodoLine(TodoItem todo)
    {
        var mark = todo.Status switch
        {
            "completed" => "[✓]",
            "in_progress" => "[•]",
            _ => "[ ]",
        };
        return mark + " " + todo.Content;
    }

    public static List<TodoItem> ParseTodos(PartItem p) => ParseTodos(p.TodoJson);

    public static List<TodoItem> ParseTodos(string json)
    {
        var list = new List<TodoItem>();
        if (string.IsNullOrEmpty(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var todo = new TodoItem
                {
                    Content = GetString(el, "content"),
                    Status = GetString(el, "status"),
                    Priority = GetString(el, "priority"),
                };
                if (todo.Content.Length > 0) list.Add(todo);
            }
        }
        catch (JsonException) { }
        return list;
    }

    public static string QuestionTitle(PartItem p) =>
        p.ToolTitle?.Length > 0 ? p.ToolTitle : ToolDisplayName(p.ToolName) ?? "Asking question...";

    /// <summary>Friendly line for a question tool that ended in an error (e.g. the user dismissed it).</summary>
    public static string QuestionError(PartItem p)
    {
        var error = p.ToolError;
        if (error.StartsWith("Tool execution failed: ", StringComparison.Ordinal))
            error = error.Substring("Tool execution failed: ".Length);
        return error.Length > 0 ? error : "Question dismissed";
    }

    public static List<QuestionItem> ParseQuestions(PartItem p) => ParseQuestions(p.Questions, p.AnswerJson);

    public static List<QuestionItem> ParseQuestions(List<Integration.QuestionInfo> questionsInfo, string answersJson)
    {
        var list = new List<QuestionItem>();
        if (questionsInfo.Count == 0) return list;
        try
        {
            var answers = ParseAnswers(answersJson);
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

    private static List<List<string>> ParseAnswers(string json)
    {
        var list = new List<List<string>>();
        if (string.IsNullOrEmpty(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var inner = new List<string>();
                if (el.ValueKind == JsonValueKind.Array)
                    foreach (var a in el.EnumerateArray())
                        if (a.GetString() is { } s && s.Length > 0) inner.Add(s);
                list.Add(inner);
            }
        }
        catch (JsonException) { }
        return list;
    }

    private static string GetString(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var prop)
            ? prop.GetString() ?? ""
            : "";

    private static int GetInt(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var prop)
            ? prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value) ? value : 0
            : 0;

    public static string WebFetch(PartItem p) =>
        "% " + (p.ToolUrl.Length > 0 ? "WebFetch " + p.ToolUrl : p.ToolTitle ?? ToolDisplayName(p.ToolName) ?? "Fetching");

    public static string Skill(PartItem p) =>
        "→ " + (p.ToolSkillName.Length > 0 ? "Skill \"" + p.ToolSkillName + "\"" : p.ToolTitle ?? ToolDisplayName(p.ToolName) ?? "Reading skill");

    public static string Read(PartItem p) =>
        "→ " + (p.ToolFilePath.Length > 0 ? "Read " + p.ToolFilePath : p.ToolTitle ?? ToolDisplayName(p.ToolName) ?? "Reading");

    public static string Loaded(PartItem p)
    {
        if (p.LoadedFiles.Length == 0) return "";
        return string.Join("\n", p.LoadedFiles.Split('\n').Select(l => "↳ Loaded " + l));
    }

    public static string Edit(PartItem p) =>
        "← " + (p.ToolFilePath.Length > 0 ? "Edit " + p.ToolFilePath : p.ToolTitle ?? ToolDisplayName(p.ToolName) ?? "Editing");

    public static string Write(PartItem p) =>
        "← " + (p.ToolFilePath.Length > 0 ? "Write " + p.ToolFilePath : p.ToolTitle ?? ToolDisplayName(p.ToolName) ?? "Writing");

    /// <summary>
    /// Edit title with an added/changed line count derived from the unified diff
    /// (the TUI itself does not surface these numbers, but the diff has enough
    /// information to compute them).
    /// </summary>
    public static string EditTitle(PartItem p)
    {
        var (added, removed) = DiffStats(p.Diff);
        if (added + removed == 0) return Edit(p);
        return $"{Edit(p)}  ({added}+ {removed}-)";
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

    public static string WriteTitle(PartItem p)
    {
        var title = Write(p);
        // The written file's content lives in input.content (ToolContent); fall back to the
        // input/output JSON when an older server only surfaces those.
        var lineCount = CountLines(p.ToolContent.Length > 0 ? p.ToolContent : p.ToolOutput.Length > 0 ? p.ToolOutput : p.ToolInput);
        return lineCount > 0 ? $"{title}  ({lineCount} lines)" : title;
    }

    public static List<PatchFileItem> ParsePatchFiles(PartItem p) => ParsePatchFiles(p.PatchJson);

    public static List<PatchFileItem> ParsePatchFiles(string json)
    {
        var list = new List<PatchFileItem>();
        if (string.IsNullOrEmpty(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var file = new PatchFileItem
                {
                    Type = GetString(el, "type"),
                    RelativePath = GetString(el, "relativePath"),
                    FilePath = GetString(el, "filePath"),
                    Patch = GetString(el, "patch"),
                    MovePath = GetString(el, "movePath"),
                    Additions = GetInt(el, "additions"),
                    Deletions = GetInt(el, "deletions"),
                };
                if (file.Type.Length > 0 && file.RelativePath.Length > 0) list.Add(file);
            }
        }
        catch (JsonException) { }
        return list;
    }

    /// <summary>
    /// Header for an <c>apply_patch</c> card: the touched file for a single-file patch,
    /// "N files" otherwise, "Preparing patch..." while in flight. Mirrors the web client's
    /// "Patch" card title/subtitle and the TUI's pending label.
    /// </summary>
    public static string Patch(PartItem p)
    {
        var files = ParsePatchFiles(p);
        if (files.Count == 1) return "← Patch " + files[0].RelativePath;
        if (files.Count > 1) return $"← Patch {files.Count} files";
        if (Busy(p)) return "Preparing patch...";
        // Server without per-file metadata: fall back to the first line of the tool title
        // (the "Success. Updated the following files:..." summary).
        var title = (p.ToolTitle ?? ToolDisplayName(p.ToolName) ?? "Patch").Split('\n')[0].Trim();
        return title.Length > 0 ? title : "Patch";
    }

    /// <summary>Per-file label mirroring the TUI's "Created/Deleted/Moved/Patched" block titles.</summary>
    public static string PatchFileLine(PatchFileItem f)
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

    public static int CountLines(string value)
    {
        if (value.Length == 0) return 0;
        var count = 1;
        foreach (var c in value)
            if (c == '\n') count++;
        return value[value.Length - 1] == '\n' ? count - 1 : count;
    }

    public static string Generic(PartItem p) =>
        "⚙ " + (p.ToolTitle ?? ToolDisplayName(p.ToolName) ?? "Running tool...");

    /// <summary>Title for a subagent-spawning <c>task</c> tool call. The state.title is the model's short description.</summary>
    public static string Task(PartItem p)
    {
        var name = p.ToolTitle?.Length > 0 ? p.ToolTitle : ToolDisplayName(p.ToolName) ?? "Delegating...";
        return "✳ " + name;
    }

    /// <summary>Status line for a <c>task</c> tool card: agent type + live state + open hint.</summary>
    public static string TaskStatus(PartItem p)
    {
        var type = p.ToolSubagentType.Length > 0 ? p.ToolSubagentType : "subagent";
        return p.ToolStatus switch
        {
            "pending" => $"Starting {type} agent…",
            "running" => $"Running {type} agent…",
            "completed" => p.ToolSessionId.Length > 0 ? "Done — click to open the session" : "Done",
            "error" => p.ToolError.Length > 0 ? $"Failed: {p.ToolError}" : "Failed",
            _ => "Click to open the session",
        };
    }

    public static string Truncate(string value, int max)
    {
        if (value.Length <= max) return value;
        return value.Substring(0, max) + "\n… (truncated, " + (value.Length - max) + " more chars)";
    }

    public const int ShellMaxLines = 10;
    public const int ShellMaxChars = ShellMaxLines * 120;

    public static bool ShellOverflow(PartItem p) => CollapseShellOutput(p).Overflow;

    public static string ShellCollapsed(PartItem p) => CollapseShellOutput(p).Output;

    private static (string Output, bool Overflow) CollapseShellOutput(PartItem p)
    {
        var output = p.ShellOutput.Length > 0 ? p.ShellOutput : p.ToolOutput;
        if (output.Length == 0) return (output, false);
        return CollapseLines(output, ShellMaxLines, ShellMaxChars);
    }

    public static bool GenericInputOverflow(PartItem p) => GenericCollapse(p.ToolInput).Overflow;

    public static string GenericInputCollapsed(PartItem p) => GenericCollapse(p.ToolInput).Output;

    public static bool GenericOutputOverflow(PartItem p) => GenericCollapse(p.ToolOutput).Overflow;

    public static string GenericOutputCollapsed(PartItem p) => GenericCollapse(p.ToolOutput).Output;

    private static (string Output, bool Overflow) GenericCollapse(string value)
    {
        if (value.Length == 0) return (value, false);
        return CollapseLines(value, ShellMaxLines, ShellMaxChars);
    }

    /// <summary>
    /// Mirrors the TUI's collapseToolOutput: keeps at most <paramref name="maxLines"/>
    /// lines and at most <paramref name="maxChars"/> characters in the preview, so a
    /// single huge line (e.g. minified JSON) still gets collapsed before it hits the
    /// layout engine (which shapes the whole string regardless of TextBlock.MaxLines).
    /// </summary>
    public static (string Output, bool Overflow) CollapseLines(string output, int maxLines, int maxChars)
    {
        var lines = output.Split('\n');
        if (lines.Length <= maxLines && output.Length <= maxChars)
            return (output, false);

        var preview = string.Join("\n", lines.Take(maxLines));
        if (preview.Length > maxChars)
            return (preview.Substring(0, Math.Max(0, maxChars - 1)) + "…", true);

        return (preview + "\n…", true);
    }

    /// <summary>
    /// Collapse helper for line-numbered views (DiffView/CodeView): returns the preview WITHOUT
    /// the trailing "…" marker (the host renders it as a separate muted line so it isn't numbered
    /// as diff/code content) plus an overflow flag.
    /// </summary>
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
}
