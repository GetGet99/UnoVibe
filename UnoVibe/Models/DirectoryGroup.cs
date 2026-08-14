using System.Collections.ObjectModel;

namespace UnoVibe.Models;

/// <summary>
/// A session list item. <c>IsExpanded</c> is a QuickMarkup reactive reference so the sidebar's
/// per-group show-more/show-less toggle updates the UI in place. Group instances are reused across
/// <c>ChatStore.ReconcileDirectoryGroups</c> (never recreated), so <c>IsExpanded</c> and
/// <c>Branch</c> live on the object and survive session list refreshes with no re-seeding.
/// <c>Branch</c> is the git branch reported by <c>GET /vcs</c> for the directory ("" when not a git
/// repo or not yet loaded); it is also a reactive reference so a <c>vcs.branch.updated</c> SSE event
/// can update the label in place.
/// </summary>
[QuickMarkup("""
    public bool IsExpanded = false;
    public string Branch = "";
    """)]
public sealed partial class DirectoryGroup
{
    public string Directory { get; set; } = "";
    public ObservableCollection<SessionInfo> Sessions { get; set; } = new();
}
