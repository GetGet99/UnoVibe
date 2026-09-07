using System.Diagnostics.CodeAnalysis;

namespace UnoVibe.Services;

[QuickMarkup("""
    bool IsRead = true; // client driven
    string Title = "";

    long Updated;
    string TimeLabel => `FormatTimeLabel(Updated)`;

    bool IsBusy;
    ChatOutcome Outcome;
    bool IsPendingQuestion;
    bool IsPendingPermission;
    SessionState State => `ResolveState()`;
    """)]
public partial class SessionHead
{
    public string Id { get; private set; }
    public string Directory { get; private set; }
    SessionState ResolveState()
    {
        if (IsBusy) return SessionState.Working;
        if (IsPendingPermission) return SessionState.PendingPermission;
        if (IsPendingQuestion) return SessionState.PendingQuestion;
        if (IsRead) return SessionState.None;
        return Outcome switch
        {
            ChatOutcome.Success => SessionState.Success,
            ChatOutcome.Interrupted => SessionState.Interrupted,
            ChatOutcome.Error => SessionState.Error,
            _ or ChatOutcome.None => SessionState.None
        };
    }

    [QuickMarkupConstructor]
    [MemberNotNull(nameof(Id), nameof(Directory))]
    void Ctor(string id, string directory)
    {
        Id = id;
        Directory = directory;
        Init(id, directory);
    }
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

public enum SessionState
{
    None,
    Working,
    PendingQuestion,
    PendingPermission,
    Success,
    Error,
    Interrupted
}
public enum ChatOutcome
{
    None,
    Success,
    Error,
    Interrupted
}