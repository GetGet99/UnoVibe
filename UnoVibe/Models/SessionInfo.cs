namespace UnoVibe.Models;

public sealed class SessionInfo
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Directory { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string Path { get; set; } = "";
    public long Updated { get; set; }
    public string Agent { get; set; } = "";
    public string ModelId { get; set; } = "";
    public string ModelProviderId { get; set; } = "";
    public string ModelVariant { get; set; } = "";

    public string TimeLabel
    {
        get
        {
            if (Updated <= 0) return "";
            var elapsed = DateTimeOffset.Now.ToUnixTimeMilliseconds() - Updated;
            var span = TimeSpan.FromMilliseconds(elapsed);
            if (span.TotalMinutes < 1) return "now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h";
            if (span.TotalDays < 30) return $"{(int)span.TotalDays}d";
            return $"{span.TotalDays / 30:0}mo";
        }
    }
}
