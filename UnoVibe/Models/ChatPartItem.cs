using Microsoft.UI.Xaml.Media.Imaging;
using QuickMarkup.Infra.Collections;
using UnoVibe.Integration.Events;

namespace UnoVibe.Models;

/// <summary>
/// Base class for all chat message parts. Each part type is a properly typed subclass
/// instead of the old monolithic PartItem "bag of fields".
/// </summary>
public abstract class ChatPartItem
{
    public string Id { get; set; } = "";
    public string MessageId { get; set; } = "";
    public abstract string Type { get; }
}

[QuickMarkup("""
    public string Text = "";
    public bool Synthetic = false;
    public bool Ignored = false;
    """)]
public partial class TextPartItem : ChatPartItem
{
    public override string Type => "text";
}

[QuickMarkup("""
    public string Text = "";
    """)]
public partial class ReasoningPartItem : ChatPartItem
{
    public override string Type => "reasoning";
    public ReasoningTime Time { get; set; }

    public string Label
    {
        get
        {
            var (title, _) = Summary;
            return title.Length > 0 ? "Thinking: " + title : "Thinking";
        }
    }

    public string ThoughtLabel
    {
        get
        {
            var (title, _) = Summary;
            var text = "Thought";
            if (title.Length > 0) text += ": " + title;
            var duration = FormatDuration(Time.DurationMs);
            if (duration.Length > 0) text += " · " + duration;
            return text;
        }
    }

    public (string Title, string Body) Summary
    {
        get
        {
            var content = Text.Replace("[REDACTED]", "").Trim();
            if (content.Length == 0) return ("", "");
            var match = System.Text.RegularExpressions.Regex.Match(content, @"^\*\*([^*\n]+)\*\*(?:\r?\n\r?\n|$)");
            if (!match.Success) return ("", content);
            return (match.Groups[1].Value.Trim(), content.Substring(match.Length).Trim());
        }
    }

    private static string FormatDuration(long ms)
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
}

[QuickMarkup("""
    using Microsoft.UI.Xaml.Media.Imaging;
    public string Mime = "";
    public string Url = "";
    public string FileName = "";
    public BitmapImage? Image;
    public bool IsImage => `Mime.StartsWith("image/")`;
    """)]
public partial class FilePartItem : ChatPartItem
{
    public override string Type => "file";

    public async Task LoadImageAsync()
    {
        if (Image is not null || !IsImage || !Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;
        var comma = Url.IndexOf(',');
        if (comma < 0) return;
        try
        {
            var bytes = Convert.FromBase64String(Url.Substring(comma + 1));
            Image = await ImageAttachment.DecodeAsync(bytes);
        }
        catch
        {
            // Leave Image null; the UI renders the file fallback.
        }
    }
}

public sealed class StepStartPartItem : ChatPartItem
{
    public override string Type => "step-start";
    public string? Snapshot { get; set; }
}

public sealed class StepFinishPartItem : ChatPartItem
{
    public override string Type => "step-finish";
    public required string Reason { get; set; }
    public string? Snapshot { get; set; }
    public double Cost { get; set; }
    public required StepFinishTokens Tokens { get; set; }
}

public sealed class SnapshotPartItem : ChatPartItem
{
    public override string Type => "snapshot";
    public required string Snapshot { get; set; }
}

public sealed class PatchPartItem : ChatPartItem
{
    public override string Type => "patch";
    public required string Hash { get; set; }
    public List<string> Files { get; set; } = [];

    public List<PatchFileItem> ParseFiles()
    {
        var list = new List<PatchFileItem>();
        foreach (var fileJson in Files)
        {
            if (string.IsNullOrEmpty(fileJson)) continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(fileJson);
                var el = doc.RootElement;
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
            catch (System.Text.Json.JsonException) { }
        }
        return list;

        static string GetString(System.Text.Json.JsonElement el, string name) =>
            el.ValueKind == System.Text.Json.JsonValueKind.Object && el.TryGetProperty(name, out var prop)
                ? prop.GetString() ?? ""
                : "";

        static int GetInt(System.Text.Json.JsonElement el, string name) =>
            el.ValueKind == System.Text.Json.JsonValueKind.Object && el.TryGetProperty(name, out var prop)
                ? prop.ValueKind == System.Text.Json.JsonValueKind.Number && prop.TryGetInt32(out var value) ? value : 0
                : 0;
    }
}

public sealed class AgentPartItem : ChatPartItem
{
    public override string Type => "agent";
    public required string Name { get; set; }
}

public sealed class RetryPartItem : ChatPartItem
{
    public override string Type => "retry";
    public double Attempt { get; set; }
    public required string ErrorMessage { get; set; }
    public ReasoningTime Time { get; set; }
}

public sealed class CompactionPartItem : ChatPartItem
{
    public override string Type => "compaction";
    public bool Auto { get; set; }
    public bool? Overflow { get; set; }
}

public sealed class SubtaskPartItem : ChatPartItem
{
    public override string Type => "subtask";
    public required string Prompt { get; set; }
    public required string Description { get; set; }
    public required string Agent { get; set; }
    public string? ModelProviderId { get; set; }
    public string? ModelModelId { get; set; }
    public string? Command { get; set; }
}

public sealed class ErrorPartItem : ChatPartItem
{
    public override string Type => "error";
    public string ErrorName { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
}

public sealed class AbortedPartItem : ChatPartItem
{
    public override string Type => "aborted";
}
