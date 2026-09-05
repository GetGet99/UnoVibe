namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>
    /// Get /session/{id}/message — returns messages with parts. The <c>Info</c> and <c>Parts</c>
    /// on each entry are <see cref="JsonElement"/> for now (full type unions deferred to a follow-up).
    /// </summary>
    public Task<Result<List<MessageWithParts>>> GetMessagesAsync(string sessionId, CancellationToken ct = default)
        => GetResultAsync($"/session/{sessionId}/message", AppJsonContext.Default.ListMessageWithParts, ct);
}
