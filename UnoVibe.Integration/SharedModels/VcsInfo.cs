namespace UnoVibe.Integration;

/// <summary>
/// VCS info from <c>GET /vcs</c>. Both fields are optional — absent when not in a git repo.
/// </summary>
public sealed class VcsInfo
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Branch { get; set; }

    [JsonPropertyName("default_branch")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultBranch { get; set; }
}
