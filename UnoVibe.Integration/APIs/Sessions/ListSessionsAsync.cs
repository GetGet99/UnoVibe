namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>
    /// Get /session — lists sessions. Without a directory the list is scoped to the server's
    /// default project; passing <paramref name="directory"/> lists sessions for that workspace.
    /// </summary>
    public async Task<Result<List<SessionInfo>>> ListSessionsAsync(CancellationToken ct = default, string? directory = null)
    {
        var url = string.IsNullOrEmpty(directory) ? "/session" : $"/session?directory={Uri.EscapeDataString(directory)}";
        return await GetResultAsync(url, AppJsonContext.Default.ListSessionInfo, ct);
    }
}
