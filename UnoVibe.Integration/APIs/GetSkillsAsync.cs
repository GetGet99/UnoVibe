namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>
    /// Lists skills available for the directory. Tries <c>/skill</c> first, falls back to
    /// <c>/api/skill</c> for older servers.
    /// </summary>
    public async Task<Result<List<SkillInfo>>> GetSkillsAsync(string? directory = null,
        CancellationToken ct = default)
    {
        var result = await GetResultAsync(
            DirectoryUrl("/skill", directory),
            AppJsonContext.Default.ListSkillInfo, ct);
        if (result.TryGetValue(out var value) && value.Count > 0) return result;

        return await GetResultAsync(
            LocationUrl("/api/skill", directory),
            AppJsonContext.Default.ListSkillInfo, ct);
    }
}
