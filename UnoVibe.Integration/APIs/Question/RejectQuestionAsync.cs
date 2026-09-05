namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>Dismisses a pending question request</summary>
    public Task RejectQuestionAsync(string requestId, string? directory = null, CancellationToken ct = default)
        => PostAsync(DirectoryUrl($"/question/{requestId}/reject", directory), ct);
}
