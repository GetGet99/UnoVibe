namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>
    /// Get /vcs — VCS info for the workspace directory. Returns the current git branch
    /// and default branch.
    /// </summary>
    public Task<Result<VcsInfo>> GetVCSInfoAsync(string? directory = null, CancellationToken ct = default)
        => GetResultAsync(DirectoryUrl("/vcs", directory), AppJsonContext.Default.VcsInfo, ct);
}
