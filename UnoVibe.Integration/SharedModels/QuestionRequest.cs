namespace UnoVibe.Integration;


/// <summary>One question inside a <see cref="PendingQuestion"/>.</summary>
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
