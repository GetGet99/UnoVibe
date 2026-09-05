namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>
    /// Get /path — returns the server instance path info including the working directory.
    /// </summary>
    public Task<Result<PathInfo>> GetDirectoryAsync(CancellationToken ct = default)
        => GetResultAsync("/path", AppJsonContext.Default.PathInfo, ct);
}
