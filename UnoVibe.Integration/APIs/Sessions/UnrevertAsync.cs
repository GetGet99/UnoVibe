namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>
    /// Post /session/{id}/unrevert — restores a reverted session (undo of undo).
    /// </summary>
    public Task UnrevertAsync(string sessionId, CancellationToken ct = default)
        => PostAsync($"/session/{sessionId}/unrevert", ct);
}
