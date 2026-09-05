namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>
    /// Lists every server/user/MCP/skill command for the directory.
    /// Tries <c>/command</c> first, falls back to <c>/api/command</c> for older servers.
    /// </summary>
    public async Task<Result<List<CommandInfo>>> GetCommandsAsync(string? directory = null,
        CancellationToken ct = default)
    {
        var result = await GetResultAsync(
            DirectoryUrl("/command", directory),
            AppJsonContext.Default.ListCommandInfo, ct);
        if (result.TryGetValue(out var value) && value.Count > 0) return result;

        return await GetResultAsync(
            LocationUrl("/api/command", directory),
            AppJsonContext.Default.ListCommandInfo, ct);
    }
}
