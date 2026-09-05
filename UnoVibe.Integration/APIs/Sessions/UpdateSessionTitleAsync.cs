namespace UnoVibe.Integration;

/// <summary>PATCH /session/{id} body (title rename).</summary>
public sealed class UpdateSessionTitleRequest
{
    public required string Title { get; set; }
}

partial class OpencodeClient
{
    /// <summary>
    /// Patch /session/{id} — writes a new title for the session.
    /// </summary>
    public Task UpdateSessionTitleAsync(string sessionId, UpdateSessionTitleRequest request, CancellationToken ct = default)
        => PatchAsync(
            $"/session/{sessionId}", request,
            AppJsonContext.Default.UpdateSessionTitleRequest, ct);
}
