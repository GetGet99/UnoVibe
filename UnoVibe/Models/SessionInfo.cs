using System.Diagnostics.CodeAnalysis;
using UnoVibe.Services;
namespace UnoVibe.Models;

/// <summary>
/// A session list item. Instances are persistent across session list refreshes (reconciled in
/// place by <c>ChatStore</c>, never recreated), so mutable display fields are QuickMarkup
/// reactive references — in-place updates (e.g. the server renames a session, or refreshes its
/// cost/token totals via <c>session.updated</c>) propagate to the UI without replacing the item
/// or rebuilding the sidebar groups. `Id` determines identity and stays plain; `Directory`
/// determines grouping and only changes when a session genuinely moves.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Services;
    public string Agent = "";
    // The server only includes a "model" object when a model is configured for the session; on
    // machines with no/limited providers it omits it (or sends null), so these must default to ""
    // instead of null — a missing model is "no model", not "crash".
    public string ModelId = "";
    public string ModelProviderId = "";
    public string ModelVariant = "";
    public double Cost;
    public long TokensInput;
    public long TokensOutput;
    public long TokensReasoning;
    public long TokensCacheRead;
    public long TokensCacheWrite;
    public long TokensTotal => `TokensInput + TokensOutput + TokensReasoning + TokensCacheRead + TokensCacheWrite`;
    """)]
public sealed partial class SessionInfo
{
    public SessionHead Head { get; private set; }
    public string Id => Head.Id;
    public string Directory => Head.Directory;
    public string ProjectId { get; set; } = "";
    public string Path { get; set; } = "";
    /// <summary>ID of the parent session when this is a subagent session (spawned by a <c>task</c> tool call), else "".</summary>
    public string ParentId { get; set; } = "";
    /// <summary>True when this session is a subagent (its server info carries a parentID).</summary>
    public bool IsSubagent => ParentId.Length > 0;

    [QuickMarkupConstructor]
    [MemberNotNull(nameof(Head))]
    void Ctor(string id, string directory)
    {
        Head = new(id, directory);
    }

    public static SessionInfo From(Integration.SessionInfo theirs)
    {
        SessionInfo sess = new(theirs.Id, theirs.Directory)
        {
            Head =
            {
                Title = theirs.Title  
            },
            ProjectId = theirs.ProjectId,
            Path = theirs.Path,
            Agent = theirs.Agent,
            ParentId = theirs.ParentId,
            Cost = theirs.Cost
        };
        if (theirs.Model is not null)
        {
            sess.ModelId = theirs.Model.Id;
            sess.ModelProviderId = theirs.Model.ProviderId;
            sess.ModelVariant = theirs.Model.Variant;
        }
        if (theirs.Time is not null)
        {
            sess.Head.Updated = theirs.Time.Updated;
        }
        if (theirs.Tokens is not null)
        {
            sess.TokensInput = theirs.Tokens.Input;
            sess.TokensOutput = theirs.Tokens.Output;
            sess.TokensReasoning = theirs.Tokens.Reasoning;
            if (theirs.Tokens.Cache is not null)
            {
                sess.TokensCacheRead = theirs.Tokens.Cache.Read;
                sess.TokensCacheWrite = theirs.Tokens.Cache.Write;
            }
        }
        return sess;
    }
}
