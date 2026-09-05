namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>(re)connects an MCP server.</summary>
    public Task McpConnectAsync(string name, string? directory = null, CancellationToken ct = default)
        => PostAsync(DirectoryUrl($"/mcp/{Uri.EscapeDataString(name)}/connect", directory), ct);
}
