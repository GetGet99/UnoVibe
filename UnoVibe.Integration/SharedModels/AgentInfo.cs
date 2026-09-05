namespace UnoVibe.Integration;

/// <summary>
/// Agent info from <c>GET /agent</c>. Trimmed to the fields this client needs.
/// </summary>
public sealed class AgentInfo
{
    public string Id { get; set; } = "";
    public string Mode { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    public bool Hidden { get; set; }
}
