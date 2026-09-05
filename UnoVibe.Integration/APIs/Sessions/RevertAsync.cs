namespace UnoVibe.Integration;

/// <summary>POST /session/{id}/revert body.</summary>
public sealed class RevertRequest
{
    public required string MessageID { get; set; }
}

partial class OpencodeClient
{
    /// <summary>
    /// Post /session/{id}/revert — rewinds the session to just before the given user
    /// message. Returns the updated <see cref="SessionInfo"/>.
    /// </summary>
    public Task<Result<SessionInfo>> RevertAsync(string sessionId, RevertRequest request, CancellationToken ct = default)
        => PostResultAsync(
            $"/session/{sessionId}/revert", request,
            AppJsonContext.Default.RevertRequest, AppJsonContext.Default.SessionInfo, ct);
}
