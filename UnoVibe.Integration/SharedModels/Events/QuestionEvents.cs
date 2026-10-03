namespace UnoVibe.Integration.Events;

#region Question V1

public sealed class QuestionAskedEvent
{
    public required string Id { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    public List<QuestionPayload> Questions { get; set; } = [];
    public QuestionEventTool? Tool { get; set; }
}

public sealed class QuestionRepliedEvent
{
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("requestID")] public required string RequestId { get; set; }
    public List<List<string>> Answers { get; set; } = [];
}

public sealed class QuestionRejectedEvent
{
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("requestID")] public required string RequestId { get; set; }
}

public sealed class QuestionEventTool
{
    [JsonPropertyName("messageID")] public required string MessageId { get; set; }
    [JsonPropertyName("callID")] public required string CallId { get; set; }
}

#endregion

#region Question V2

public sealed class QuestionV2AskedEvent
{
    public required string Id { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    public List<QuestionPayload> Questions { get; set; } = [];
    public QuestionEventTool? Tool { get; set; }
}

public sealed class QuestionV2RepliedEvent
{
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("requestID")] public required string RequestId { get; set; }
    public List<List<string>> Answers { get; set; } = [];
}

public sealed class QuestionV2RejectedEvent
{
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("requestID")] public required string RequestId { get; set; }
}

#endregion

#region Shared question payload

public sealed class QuestionPayload
{
    public required string Question { get; set; }
    public required string Header { get; set; }
    public List<QuestionOptionPayload> Options { get; set; } = [];
    public bool? Multiple { get; set; }
    public bool? Custom { get; set; }
}

public sealed class QuestionOptionPayload
{
    public required string Label { get; set; }
    public required string Description { get; set; }
}

#endregion
