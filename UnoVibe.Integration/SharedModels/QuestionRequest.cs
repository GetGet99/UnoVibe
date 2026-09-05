namespace UnoVibe.Integration;

/// <summary>
/// Pending question request from <c>GET /question</c>.
/// </summary>
public sealed class QuestionRequest
{
    public string Id { get; set; } = "";

    [JsonPropertyName("sessionID")]
    public string SessionId { get; set; } = "";

    public List<QuestionInfo> Questions { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public QuestionToolInfo? Tool { get; set; }
}

/// <summary>One question inside a <see cref="QuestionRequest"/>.</summary>
public sealed class QuestionInfo
{
    public string Question { get; set; } = "";
    public string Header { get; set; } = "";
    public List<QuestionOption> Options { get; set; } = [];
    public bool Multiple { get; set; }
    public bool Custom { get; set; }
}

/// <summary>One selectable option in a <see cref="QuestionInfo"/>.</summary>
public sealed class QuestionOption
{
    public string Label { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>Tool context linking a question to a specific tool call.</summary>
public sealed class QuestionToolInfo
{
    [JsonPropertyName("messageID")]
    public string MessageId { get; set; } = "";

    [JsonPropertyName("callID")]
    public string CallId { get; set; } = "";
}
