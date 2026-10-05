namespace UnoVibe.Models;

sealed class RecentConnection
{
    public const string FolderKind = "Folder";
    public const string ServerKind = "Server";

    public string Kind { get; set; } = FolderKind;

    public string Key { get; set; } = "";

    public string Display { get; set; } = "";

    public string Detail { get; set; } = "";

    public long LastOpenedUnix { get; set; }

    public bool RequiresPassword { get; set; }

    public bool IsFolder => Kind == FolderKind;
}
