using System.Text.Json;
using UnoVibe.Models;

namespace UnoVibe.Controls.ToolViews;

public static class ToolViewShared
{
    public static string Shell(PartItem p) =>
        p.ToolCommand.Length > 0
            ? "$ " + p.ToolCommand + (p.ToolWorkdir.Length > 0 ? "  (in " + p.ToolWorkdir + ")" : "")
            : p.ToolTitle ?? p.ToolName ?? "shell";

    public static string Glob(PartItem p)
    {
        var name = p.ToolPattern.Length > 0 ? "Glob \"" + p.ToolPattern + "\"" : p.ToolTitle ?? p.ToolName ?? "glob";
        var count = p.MatchCount.Length > 0 ? " (" + p.MatchCount + " match" + (p.MatchCount == "1" ? "" : "es") + ")" : "";
        return "✱ " + name + count;
    }

    public static string Grep(PartItem p)
    {
        var name = p.ToolPattern.Length > 0 ? "Grep \"" + p.ToolPattern + "\"" : p.ToolTitle ?? p.ToolName ?? "grep";
        if (p.ToolSearchPath.Length > 0) name += " in " + p.ToolSearchPath;
        if (p.ToolInclude.Length > 0) name += " (" + p.ToolInclude + ")";
        var count = p.MatchCount.Length > 0 ? " (" + p.MatchCount + " match" + (p.MatchCount == "1" ? "" : "es") + ")" : "";
        return "✱ " + name + count;
    }

    public static string TodoTitle(PartItem p) =>
        p.ToolTitle?.Length > 0 ? p.ToolTitle : p.ToolName ?? "todos";

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
        p.ToolTitle?.Length > 0 ? p.ToolTitle : p.ToolName ?? "question";

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
        "% " + (p.ToolUrl.Length > 0 ? "WebFetch " + p.ToolUrl : p.ToolTitle ?? p.ToolName ?? "webfetch");

    public static string Read(PartItem p) =>
        "→ " + (p.ToolFilePath.Length > 0 ? "Read " + p.ToolFilePath : p.ToolTitle ?? p.ToolName ?? "read");

    public static string Loaded(PartItem p)
    {
        if (p.LoadedFiles.Length == 0) return "";
        return string.Join("\n", p.LoadedFiles.Split('\n').Select(l => "↳ Loaded " + l));
    }

    public static string Edit(PartItem p) =>
        "← " + (p.ToolFilePath.Length > 0 ? "Edit " + p.ToolFilePath : p.ToolTitle ?? p.ToolName ?? "edit");

    public static string Write(PartItem p) =>
        "← " + (p.ToolFilePath.Length > 0 ? "Write " + p.ToolFilePath : p.ToolTitle ?? p.ToolName ?? "write");

    public static string Generic(PartItem p)
    {
        var name = p.ToolTitle ?? p.ToolName ?? "tool";
        var input = p.ToolInput.Length > 0 && p.ToolInput.Length <= 400 ? " " + p.ToolInput : "";
        return "⚙ " + name + input;
    }

    public static string Truncate(string value, int max)
    {
        if (value.Length <= max) return value;
        return value.Substring(0, max) + "\n… (truncated, " + (value.Length - max) + " more chars)";
    }
}
