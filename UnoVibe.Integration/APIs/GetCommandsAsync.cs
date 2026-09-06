namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>
    /// Lists every server/user/MCP/skill command for the directory.
    /// Tries <c>/command</c> first (legacy bare array), falls back to <c>/api/command</c>
    /// (v2 <c>{ location, data }</c> envelope) for older servers.
    /// </summary>
    public async Task<Result<APIEntryResponse<List<CommandInfo>>>> GetCommandsAsync(string? directory = null,
        CancellationToken ct = default)
    {
        var legacy = await GetResultAsync(
            DirectoryUrl("/command", directory),
            AppJsonContext.Default.ListCommandInfo, ct);
        if (legacy.TryGetValue(out var value) && value.Count > 0)
            return Result<APIEntryResponse<List<CommandInfo>>>.Success(
                new APIEntryResponse<List<CommandInfo>> { Data = value });

        return await GetResultAsync(
            LocationUrl("/api/command", directory),
            AppJsonContext.Default.APIEntryResponseListCommandInfo, ct);
    }
}
