namespace UnoVibe.Integration;

/// <summary>POST /question/{requestId}/reply body.</summary>
public sealed class ReplyQuestionRequest
{
    public required IReadOnlyList<IReadOnlyList<string>> Answers { get; set; }
}

partial class OpencodeClient
{
    /// <summary>
    /// Answers a pending question request.
    /// </summary>
    public Task ReplyQuestionAsync(string requestId, ReplyQuestionRequest request,
        string? directory = null, CancellationToken ct = default)
        => PostAsync(
            DirectoryUrl($"/question/{requestId}/reply", directory),
            request,
            AppJsonContext.Default.ReplyQuestionRequest,
            ct
        );
}
