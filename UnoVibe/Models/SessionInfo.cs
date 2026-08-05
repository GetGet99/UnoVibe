namespace UnoVibe.Models;

/// <summary>
/// A session list item. Mutable display fields are QuickMarkup reactive references so
/// in-place updates (e.g. the server renames a session, or refreshes its cost/token
/// totals via <c>session.updated</c>) propagate to the UI without replacing the item
/// or rebuilding the sidebar groups. `Id`/`Directory` determine identity/grouping and
/// stay plain (they never change for a live session).
/// </summary>
[QuickMarkup("""
    public string Title;
    public long Updated;
    public string Agent;
    public string ModelId;
    public string ModelProviderId;
    public string ModelVariant;
    public double Cost;
    public long TokensInput;
    public long TokensOutput;
    public long TokensReasoning;
    public long TokensCacheRead;
    public long TokensCacheWrite;
    public long TokensTotal => `TokensInput + TokensOutput + TokensReasoning + TokensCacheRead + TokensCacheWrite`;
    public string TimeLabel => `FormatTimeLabel(Updated)`;
    // Server-driven: a turn is in progress (session.status busy/retry). Drives the sidebar spinner.
    public bool IsBusy;
    // Client-side: a turn finished in a session we haven't viewed since. Drives the sidebar unread indicator.
    public bool IsUnread;
    // Client-side: how the last finished turn ended, one of "" (unknown), "success", "error", "interrupted".
    // Derived from the last assistant message's info.error/finish; selects the sidebar icon + color.
    public string Outcome = "";
    // Client-side: a question is asked or an approval is pending for this session and the user
    // hasn't answered it yet. Overrides the busy spinner in the sidebar (mirrors the web client's
    // needsAttention) and shows a distinct glyph.
    public bool NeedsAttention;
    // Which kind of attention is pending: "permission" (approval needed), "question", or "" when none.
    public string AttentionKind = "";
    """)]
public sealed partial class SessionInfo
{
    public string Id { get; set; } = "";
    public string Directory { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string Path { get; set; } = "";

    // QuickMarkup Computed<string> (backing field TimeLabelComp): reads the reactive `Updated`
    // field, so it caches and re-evaluates automatically whenever Updated changes — the sidebar's
    // `s.TimeLabel` binding updates without any manual rebuild.
    private static string FormatTimeLabel(long updated)
    {
        if (updated <= 0) return "";
        var elapsed = DateTimeOffset.Now.ToUnixTimeMilliseconds() - updated;
        var span = TimeSpan.FromMilliseconds(elapsed);
        if (span.TotalMinutes < 1) return "now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays}d";
        return $"{span.TotalDays / 30:0}mo";
    }
}
