using System.Text.Json;
using System.Text.RegularExpressions;
using UnoVibe.Models;

namespace UnoVibe.Controls.ToolViews;

public static class ToolViewShared
{
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
            ? "$ " + p.ToolCommand + (p.ToolWorkdir.Length > 0 ? "  (in " + p.ToolWorkdir + ")" : "")
            : p.ToolTitle ?? p.ToolName ?? "Running command...";

    public static string Glob(PartItem p)
    {
        var name = p.ToolPattern.Length > 0 ? "Glob \"" + p.ToolPattern + "\"" : p.ToolTitle ?? p.ToolName ?? "Globbing...";
        var count = p.MatchCount.Length > 0 ? " (" + p.MatchCount + " match" + (p.MatchCount == "1" ? "" : "es") + ")" : "";
        return "✱ " + name + count;
    }

    public static string Grep(PartItem p)
    {
        var name = p.ToolPattern.Length > 0 ? "Grep \"" + p.ToolPattern + "\"" : p.ToolTitle ?? p.ToolName ?? "Grepping...";
        if (p.ToolSearchPath.Length > 0) name += " in " + p.ToolSearchPath;
        if (p.ToolInclude.Length > 0) name += " (" + p.ToolInclude + ")";
        var count = p.MatchCount.Length > 0 ? " (" + p.MatchCount + " match" + (p.MatchCount == "1" ? "" : "es") + ")" : "";
        return "✱ " + name + count;
    }

    public static string TodoTitle(PartItem p) =>
        p.ToolTitle?.Length > 0 ? p.ToolTitle : p.ToolName ?? "Writing todos...";

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
        p.ToolTitle?.Length > 0 ? p.ToolTitle : p.ToolName ?? "Asking question...";

    public static List<QuestionItem> ParseQuestions(PartItem p) => ParseQuestions(p.QuestionJson, p.AnswerJson);

    public static List<QuestionItem> ParseQuestions(string questionsJson, string answersJson)
    {
        var list = new List<QuestionItem>();
        if (string.IsNullOrEmpty(questionsJson)) return list;
        try
        {
            var answers = ParseAnswers(answersJson);
            using var doc = JsonDocument.Parse(questionsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            var i = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var q = new QuestionItem
                {
                    Question = GetString(el, "question"),
                    Header = GetString(el, "header"),
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

    public static string WebFetch(PartItem p) =>
        "% " + (p.ToolUrl.Length > 0 ? "WebFetch " + p.ToolUrl : p.ToolTitle ?? p.ToolName ?? "Fetching");

    public static string Skill(PartItem p) =>
        "→ " + (p.ToolSkillName.Length > 0 ? "Skill \"" + p.ToolSkillName + "\"" : p.ToolTitle ?? p.ToolName ?? "Reading skill");

    public static string Read(PartItem p) =>
        "→ " + (p.ToolFilePath.Length > 0 ? "Read " + p.ToolFilePath : p.ToolTitle ?? p.ToolName ?? "Reading");

    public static string Loaded(PartItem p)
    {
        if (p.LoadedFiles.Length == 0) return "";
        return string.Join("\n", p.LoadedFiles.Split('\n').Select(l => "↳ Loaded " + l));
    }

    public static string Edit(PartItem p) =>
        "← " + (p.ToolFilePath.Length > 0 ? "Edit " + p.ToolFilePath : p.ToolTitle ?? p.ToolName ?? "Editing");

    public static string Write(PartItem p) =>
        "← " + (p.ToolFilePath.Length > 0 ? "Write " + p.ToolFilePath : p.ToolTitle ?? p.ToolName ?? "Writing");

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
        var lineCount = CountLines(p.ToolOutput.Length > 0 ? p.ToolOutput : p.ToolInput);
        return lineCount > 0 ? $"{title}  ({lineCount} lines)" : title;
    }

    public static int CountLines(string value)
    {
        if (value.Length == 0) return 0;
        var count = 1;
        foreach (var c in value)
            if (c == '\n') count++;
        return value[value.Length - 1] == '\n' ? count - 1 : count;
    }

    public static string Generic(PartItem p)
    {
        var name = p.ToolTitle ?? p.ToolName ?? "Running tool...";
        var input = p.ToolInput.Length > 0 && p.ToolInput.Length <= 400 ? " " + p.ToolInput : "";
        return "⚙ " + name + input;
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
}
