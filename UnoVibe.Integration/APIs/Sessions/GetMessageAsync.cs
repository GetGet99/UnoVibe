namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>
    /// Get /session/{id}/message/{messageId} — returns a single message with its parts.
    /// </summary>
    public Task<Result<MessageWithParts>> GetMessageAsync(string sessionId, string messageId, CancellationToken ct = default)
        => GetResultAsync($"/session/{sessionId}/message/{messageId}", AppJsonContext.Default.MessageWithParts, ct);
}
