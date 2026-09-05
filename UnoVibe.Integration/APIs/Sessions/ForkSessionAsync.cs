namespace UnoVibe.Integration;

/// <summary>POST /session/{id}/fork body: empty for a full-session fork, <c>{messageID}</c> otherwise.</summary>
public sealed class ForkSessionRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MessageID { get; set; }
}

partial class OpencodeClient
{
    /// <summary>
    /// Post /session/{id}/fork — creates a new session by forking an existing one at a
    /// specific message point. Body is either empty (full-session fork) or <c>{ messageID }</c>;
    /// the server copies all messages strictly before the fork point and titles the new session
    /// "&lt;original&gt; (fork #N)". Returns the new session's info.
    /// </summary>
    public Task<Result<SessionInfo>> ForkSessionAsync(string sessionId, ForkSessionRequest request,
        CancellationToken ct = default)
        => PostResultAsync(
            $"/session/{sessionId}/fork", request,
            AppJsonContext.Default.ForkSessionRequest, AppJsonContext.Default.SessionInfo, ct);
}
