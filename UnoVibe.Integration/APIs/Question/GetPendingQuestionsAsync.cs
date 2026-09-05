namespace UnoVibe.Integration;

/// <summary>
/// Pending question request from <c>GET /question</c>.
/// </summary>
public sealed class PendingQuestion
{
    public string Id { get; set; } = "";

    [JsonPropertyName("sessionID")]
    public string SessionId { get; set; } = "";

    public List<QuestionInfo> Questions { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public QuestionToolInfo? Tool { get; set; }
}

partial class OpencodeClient
{
    /// <summary>
    /// Get /question?directory= — lists pending question requests for the workspace directory.
    /// Each request contains its questions, options, and optional tool context.
    /// </summary>
    public Task<Result<List<PendingQuestion>>> GetPendingQuestionsAsync(string? directory = null, CancellationToken ct = default)
        => GetResultAsync(DirectoryUrl("/question", directory), AppJsonContext.Default.ListPendingQuestion, ct);
}
