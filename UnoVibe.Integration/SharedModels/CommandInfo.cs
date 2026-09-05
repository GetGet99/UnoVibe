namespace UnoVibe.Integration;

/// <summary>
/// One command from <c>GET /command</c>. Matches the wire type plus optional
/// server-extension fields (<c>source</c>, <c>hints</c>).
/// </summary>
public sealed class CommandInfo
{
    public string Name { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Agent { get; set; }

    public bool Subtask { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; set; }

    public string[] Hints { get; set; } = [];
}
