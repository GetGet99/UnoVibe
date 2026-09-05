namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>
    /// Get /session/{id} — fetches a single session's info.
    /// </summary>
    public Task<Result<SessionInfo>> GetSessionAsync(string sessionId, CancellationToken ct = default)
        => GetResultAsync($"/session/{sessionId}", AppJsonContext.Default.SessionInfo, ct);
}
