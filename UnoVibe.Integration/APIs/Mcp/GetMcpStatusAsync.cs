namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>
    /// Get /mcp — status of all MCP servers for the given workspace directory.
    /// Returns a map of server name → <see cref="McpStatusInfo"/>.
    /// </summary>
    public Task<Result<Dictionary<string, McpStatusInfo>>> GetMcpStatusAsync(string? directory = null,
        CancellationToken ct = default)
        => GetResultAsync(DirectoryUrl("/mcp", directory), AppJsonContext.Default.DictionaryStringMcpStatusInfo, ct);
}
