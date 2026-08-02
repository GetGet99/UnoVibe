using System.Collections.ObjectModel;

namespace UnoVibe.Models;

public sealed class DirectoryGroup
{
    public string Directory { get; set; } = "";
    public ObservableCollection<SessionInfo> Sessions { get; set; } = new();
}
