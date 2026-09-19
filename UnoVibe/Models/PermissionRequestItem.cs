using System.Text.Json;
using UnoVibe.Integration.Events;

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

    /// <summary>Creates from a typed <c>GET /permission</c> response DTO.</summary>
    public static PermissionRequestItem From(Integration.PermissionRequestDto theirs)
    {
        var item = new PermissionRequestItem
        {
            Id = theirs.Id,
            SessionId = theirs.SessionId,
            Permission = theirs.Permission,
            Patterns = theirs.Patterns,
            Always = theirs.Always
        };

        if (theirs.Tool is not null)
        {
            item.ToolMessageId = theirs.Tool.MessageId;
            item.ToolCallId = theirs.Tool.CallId;
        }

        var meta = ParseMetadata(theirs.Metadata);
        (item.Title, item.Body) = Describe(item.Permission, meta, item.Patterns);
        item.PatternsText = FormatPatterns(item.Patterns);
        item.AlwaysText = FormatPatterns(item.Always);
        return item;
    }

    /// <summary>Creates from a typed <c>permission.asked</c> SSE event.</summary>
    public static PermissionRequestItem From(PermissionAskedEvent e)
    {
        var item = new PermissionRequestItem
        {
            Id = e.Id,
            SessionId = e.SessionId,
            Permission = e.Permission,
            Patterns = e.Patterns.ToArray(),
            Always = e.Always.ToArray()
        };

        if (e.Tool is not null)
        {
            item.ToolMessageId = e.Tool.MessageId;
            item.ToolCallId = e.Tool.CallId;
        }

        var meta = ParseMetadata(e.Metadata);
        (item.Title, item.Body) = Describe(item.Permission, meta, item.Patterns);
        item.PatternsText = FormatPatterns(item.Patterns);
        item.AlwaysText = FormatPatterns(item.Always);
        return item;
    }

    static Dictionary<string, string> ParseMetadata(JsonElement metadata)
    {
        var meta = new Dictionary<string, string>();
        if (metadata.ValueKind == JsonValueKind.Object)
            foreach (var p in metadata.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String)
                    meta[p.Name] = p.Value.GetString() ?? "";
        return meta;
    }

    static string FormatPatterns(string[] patterns) =>
        string.Join("\n", patterns.Where(p => p.Length > 0).Select(p => "• " + p));

    static string S(string key, Dictionary<string, string> meta) =>
        meta.TryGetValue(key, out var v) ? v : "";

    /// <summary>Builds a compact "Title" + "Body" description from the tool metadata.</summary>
    static (string Title, string Body) Describe(string permission, Dictionary<string, string> meta, string[] patterns)
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
            {
                var cmd = S("command", meta);
                return ("Shell command", cmd.Length > 0 ? "$ " + cmd : "");
            }
            case "external_directory":
            {
                var parent = S("parentDir", meta);
                var filepath = S("filepath", meta);
                var firstPattern = patterns.FirstOrDefault(p => p.Length > 0) ?? "";
                var derived = firstPattern.Length > 0 && firstPattern.Contains('*')
                    ? System.IO.Path.GetDirectoryName(firstPattern) ?? firstPattern
                    : firstPattern;
                var dir = parent.Length > 0 ? parent : filepath.Length > 0 ? filepath : derived;
                return ("Access external directory " + dir, "");
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

    static string Truncate(string value, int max)
    {
        if (value.Length <= max) return value;
        return string.Concat(value.AsSpan(0, max), "\n… (truncated)");
    }
}
