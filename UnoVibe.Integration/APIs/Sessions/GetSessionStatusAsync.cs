namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>
    /// Get /session/status — snapshot map of session statuses. Each value is a
    /// <see cref="SessionStatusInfo"/> discriminated union (<c>"idle"</c>, <c>"busy"</c>,
    /// or <c>"retry"</c> with attempt/message/next). Idle sessions may be absent.
    /// </summary>
    public Task<Result<Dictionary<string, SessionStatusInfo>>> GetSessionStatusAsync(CancellationToken ct = default)
        => GetResultAsync("/session/status", AppJsonContext.Default.DictionaryStringSessionStatusInfo, ct);
}
