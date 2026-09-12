using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace UnoVibe.Models;

[QuickMarkup("""
    bool IsRead = true; // client driven
    
    string Directory = `null!`;

    string Title = "";

    long Updated;
    string TimeLabel => `FormatTimeLabel(Updated)`;

    bool IsBusy;
    ChatOutcome Outcome;
    bool IsPendingQuestion;
    bool IsPendingPermission;
    SessionState State => `ResolveState()`;

    // Refers to selected values
    ChatParameters ChatParams = `new()`;
    """)]
public partial class SessionHead
{
    public SessionId Id { get; private set; }
    /// <summary>ID of the parent session when this is a subagent session (spawned by a <c>task</c> tool call), else "".</summary>
    public SessionId? ParentId { get; set; }
    /// <summary>True when this session is a subagent (its server info carries a parentID).</summary>
    public bool IsSubagent => ParentId is not null;
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
    void Ctor(SessionId id, string directory)
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

    public static SessionHead From(Integration.SessionInfo theirs)
    {
        SessionHead sess = new(new(theirs.Id), theirs.Directory)
        {
            Title = theirs.Title,
            ChatParams = {
                Agent = theirs.Agent,
            }
        };
        if (theirs.Model is {} model)
        {
            sess.ChatParams.Model = new(model.ProviderId, model.Id);
            sess.ChatParams.Variant = model.Variant;
        }
        if (theirs.Time is not null) sess.Updated = theirs.Time.Updated;
        return sess;
    }

    public void ApplyUpdateFrom(Integration.SessionInfo fresh)
    {
        Debug.Assert(Id == fresh.Id);
        Directory = fresh.Directory;
        Title = fresh.Title;
        if (fresh.Model is {} model)
        {
            ChatParams.Model = new(model.ProviderId, model.Id);
            ChatParams.Variant = model.Variant;
        } else
        {
            ChatParams.Model = null;
            ChatParams.Variant = null;
        }
        if (fresh.Time is not null) Updated = fresh.Time.Updated;
    }
}
