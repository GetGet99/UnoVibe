namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>
    /// Get /session/{id}/message/{messageId} — returns a single message with its parts.
    /// </summary>
    // TODO [Medium]: No directory param while SDK SessionMessageData carries query.directory — may 404 for folder-opened instances. Add string? directory like permission/question endpoints or document why global.
    public Task<Result<MessageWithParts>> GetMessageAsync(string sessionId, string messageId, CancellationToken ct = default)
        => GetResultAsync($"/session/{sessionId}/message/{messageId}", AppJsonContext.Default.MessageWithParts, ct);
}
