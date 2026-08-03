using System.Text.Json;

namespace UnoVibe.Models;

/// <summary>
/// A reactive model for a pending permission request (<c>permission.asked</c>).
/// Carries a human-readable <see cref="Title"/> / <see cref="Body"/> derived from the
/// tool metadata so the UI can render an allow/reject prompt without knowing tool internals.
/// </summary>
[QuickMarkup("""
    public string Id = "";
    public string SessionId = "";
    public string Permission = "";
    public string Title = "";
    public string Body = "";
    public string PatternsText = "";
    public string AlwaysText = "";
    """)]
public partial class PermissionRequestItem
{
    public string[] Patterns { get; set; } = Array.Empty<string>();
    public string[] Always { get; set; } = Array.Empty<string>();
    public string ToolMessageId { get; set; } = "";
    public string ToolCallId { get; set; } = "";

    public static PermissionRequestItem FromJson(JsonElement request)
    {
        var permission = GetString(request, "permission");
        var item = new PermissionRequestItem
        {
            Id = GetString(request, "id"),
            SessionId = GetString(request, "sessionID"),
            Permission = permission,
            Patterns = GetStringArray(request, "patterns"),
            Always = GetStringArray(request, "always"),
        };

        if (request.TryGetProperty("tool", out var tool) && tool.ValueKind == JsonValueKind.Object)
        {
            item.ToolMessageId = GetString(tool, "messageID");
            item.ToolCallId = GetString(tool, "callID");
        }

        var meta = new Dictionary<string, JsonElement>();
        if (request.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
            foreach (var p in metadata.EnumerateObject()) meta[p.Name] = p.Value;

        (item.Title, item.Body) = Describe(permission, meta, item.Patterns);

        item.PatternsText = string.Join("\n", item.Patterns.Where(p => p.Length > 0).Select(p => "• " + p));
        item.AlwaysText = string.Join("\n", item.Always.Where(p => p.Length > 0).Select(p => "• " + p));
        return item;
    }

    private static string S(string key, Dictionary<string, JsonElement> meta) =>
        meta.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>Builds a compact "Title" + "Body" description from the tool metadata.</summary>
    private static (string Title, string Body) Describe(string permission, Dictionary<string, JsonElement> meta, string[] patterns)
    {
        string first() => patterns.FirstOrDefault(p => p.Length > 0) ?? "";

        switch (permission)
        {
            case "edit":
            case "write":
            case "apply_patch":
            {
                var path = S("filepath", meta);
                if (path.Length == 0) path = first();
                var diff = S("diff", meta);
                return ("Edit " + path, Truncate(diff, 2000));
            }
            case "read":
            {
                var path = S("filePath", meta);
                if (path.Length == 0) path = first();
                return ("Read " + path, "");
            }
            case "bash":
            case "shell":
            case "external_directory":
            {
                var cmd = S("command", meta);
                return ("Shell command", cmd.Length > 0 ? "$ " + cmd : "");
            }
            case "glob":
                return ("Glob " + S("pattern", meta), "");
            case "grep":
                return ("Grep " + S("pattern", meta), "");
            case "list":
                return ("List " + S("path", meta), "");
            case "webfetch":
                return ("WebFetch " + S("url", meta), "");
            case "websearch":
                return ("WebSearch " + S("query", meta), "");
            case "task":
                return ("Agent task", S("description", meta));
            case "skill":
                return ("Run skill " + S("name", meta), "");
            case "todowrite":
                return ("Update todos", "");
            case "doom_loop":
                return ("Continue after repeated failures", "");
            default:
                return ("Call tool " + permission, "");
        }
    }

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max) return value;
        return value.Substring(0, max) + "\n… (truncated)";
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var prop) ? prop.GetString() ?? "" : "";

    private static string[] GetStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        return prop.EnumerateArray().Select(p => p.GetString() ?? "").Where(s => s.Length > 0).ToArray();
    }
}
