namespace UnoVibe.Integration;

/// <summary>
/// One skill from <c>GET /skill</c>. Trimmed to the fields this client needs.
/// </summary>
public sealed class SkillInfo
{
    public string Name { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
}
