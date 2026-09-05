namespace UnoVibe.Integration;

/// <summary>
/// Instance path info from <c>GET /path</c>. All fields are required.
/// </summary>
public sealed class PathInfo
{
    public string Home { get; set; } = "";
    public string State { get; set; } = "";
    public string Config { get; set; } = "";
    public string Worktree { get; set; } = "";
    public string Directory { get; set; } = "";
}
