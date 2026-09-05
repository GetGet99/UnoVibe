namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>
    /// Get /question?directory= — lists pending question requests for the workspace directory.
    /// Each request contains its questions, options, and optional tool context.
    /// </summary>
    public Task<Result<List<QuestionRequest>>> GetPendingQuestionsAsync(string? directory = null, CancellationToken ct = default)
        => GetResultAsync(DirectoryUrl("/question", directory), AppJsonContext.Default.ListQuestionRequest, ct);
}
