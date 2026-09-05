namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>disconnects an MCP server.</summary>
    public Task McpDisconnectAsync(string name, string? directory = null, CancellationToken ct = default)
        => PostAsync(DirectoryUrl($"/mcp/{Uri.EscapeDataString(name)}/disconnect", directory), ct);
}
