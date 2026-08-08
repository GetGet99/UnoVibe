using System.Collections.ObjectModel;

namespace UnoVibe.Models;

/// <summary>
/// A session list item. <c>IsExpanded</c> is a QuickMarkup reactive reference so the sidebar's
/// per-group show-more/show-less toggle updates the UI in place. The expanded state is owned by
/// <c>ChatStore</c> (keyed by directory) so it survives sidebar group rebuilds.
/// </summary>
[QuickMarkup("""
    public bool IsExpanded = false;
    """)]
public sealed partial class DirectoryGroup
{
    public string Directory { get; set; } = "";
    public ObservableCollection<SessionInfo> Sessions { get; set; } = new();
}
