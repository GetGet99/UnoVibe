namespace UnoVibe.Integration;

/// <summary>
/// MCP server status entry from <c>GET /mcp</c> or <c>POST /mcp/{name}/auth/authenticate</c>.
/// </summary>
public sealed class McpStatusInfo
{
    public string Status { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
}
