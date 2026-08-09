namespace UnoVibe.Models;

/// <summary>
/// One entry in the ConnectPage "Recent" list: either a local folder the user
/// launched `opencode serve` in, or an existing server URL they connected to.
/// Plain DTO — persisted as JSON by <c>RecentConnectionsStore</c>.
/// </summary>
public sealed class RecentConnection
{
    public const string FolderKind = "Folder";
    public const string ServerKind = "Server";

    /// <summary><see cref="FolderKind"/> or <see cref="ServerKind"/>.</summary>
    public string Kind { get; set; } = FolderKind;

    /// <summary>Normalized identity for dedup: full folder path or server URL.</summary>
    public string Key { get; set; } = "";

    /// <summary>Short display name (folder name for folders, URL for servers).</summary>
    public string Display { get; set; } = "";

    /// <summary>Full detail line (folder path or full URL).</summary>
    public string Detail { get; set; } = "";

    /// <summary>Unix-seconds of last successful open; drives ordering (most recent first).</summary>
    public long LastOpenedUnix { get; set; }

    /// <summary>
    /// For servers: whether the server requires a password. The password itself is never
    /// persisted — reopening an entry with this flag prompts for the password instead.
    /// </summary>
    public bool RequiresPassword { get; set; }

    public bool IsFolder => Kind == FolderKind;
}
