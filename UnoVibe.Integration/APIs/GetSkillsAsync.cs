namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>
    /// Lists skills available for the directory. Tries <c>/skill</c> first (legacy bare array),
    /// falls back to <c>/api/skill</c> (v2 <c>{ location, data }</c> envelope) for older servers.
    /// </summary>
    public async Task<Result<APIEntryResponse<List<SkillInfo>>>> GetSkillsAsync(string? directory = null,
        CancellationToken ct = default)
    {
        var legacy = await GetResultAsync(
            DirectoryUrl("/skill", directory),
            AppJsonContext.Default.ListSkillInfo, ct);
        if (legacy.TryGetValue(out var value) && value.Count > 0)
            return Result<APIEntryResponse<List<SkillInfo>>>.Success(
                new APIEntryResponse<List<SkillInfo>> { Data = value });

        return await GetResultAsync(
            LocationUrl("/api/skill", directory),
            AppJsonContext.Default.APIEntryResponseListSkillInfo, ct);
    }
}
