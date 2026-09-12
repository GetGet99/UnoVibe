namespace UnoVibe.Integration.Events;

#region Message events

public sealed class MessageUpdatedEvent
{
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    public required MessageInfo Info { get; set; }
}

public sealed class MessageRemovedEvent
{
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("messageID")] public required string MessageId { get; set; }
}

public sealed class MessagePartUpdatedEvent
{
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    public required Part Part { get; set; }
    public double Time { get; set; }
}

public sealed class MessagePartRemovedEvent
{
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("messageID")] public required string MessageId { get; set; }
    [JsonPropertyName("partID")] public required string PartId { get; set; }
}

public sealed class MessagePartDeltaEvent
{
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("messageID")] public required string MessageId { get; set; }
    [JsonPropertyName("partID")] public required string PartId { get; set; }
    public required string Field { get; set; }
    public required string Delta { get; set; }
}

#endregion

#region MessageInfo hierarchy

public sealed class UserMessageInfo : MessageInfo
{
    public required string Id { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    public required UserMessageTime Time { get; set; }
    public OutputFormat? Format { get; set; }
    public UserMessageSummary? Summary { get; set; }
    public required string Agent { get; set; }
    public required SessionModelInfo Model { get; set; }
    public string? System { get; set; }
    public Dictionary<string, bool>? Tools { get; set; }
}

public sealed class UserMessageTime
{
    public double Created { get; set; }
}

public sealed class OutputFormat
{
    public string Type { get; set; } = "text";
    public JsonElement? Schema { get; set; }
    public double? RetryCount { get; set; }
}

public sealed class UserMessageSummary
{
    public string? Title { get; set; }
    public string? Body { get; set; }
    public List<SnapshotFileDiff> Diffs { get; set; } = [];
}

public sealed class AssistantMessageInfo : MessageInfo
{
    public required string Id { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    public required AssistantMessageTime Time { get; set; }
    [JsonPropertyName("parentID")] public required string ParentId { get; set; }
    [JsonPropertyName("modelID")] public required string ModelId { get; set; }
    [JsonPropertyName("providerID")] public required string ProviderId { get; set; }
    public required string Mode { get; set; }
    public required string Agent { get; set; }
    public required AssistantPath Path { get; set; }
    public bool? Summary { get; set; }
    public double Cost { get; set; }
    public required TokenUsage Tokens { get; set; }
    public JsonElement? Structured { get; set; }
    public string? Variant { get; set; }
    public string? Finish { get; set; }
    public AssistantError? Error { get; set; }
}

public sealed class AssistantMessageTime
{
    public double Created { get; set; }
    public double? Completed { get; set; }
}

#endregion

#region Part types

public sealed class TextPart : Part
{
    public required string Text { get; set; }
    public bool? Synthetic { get; set; }
    public bool? Ignored { get; set; }
    public TimeRange? Time { get; set; }
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}

public sealed class ReasoningPart : Part
{
    public required string Text { get; set; }
    public Dictionary<string, JsonElement>? Metadata { get; set; }
    public required TimeRange Time { get; set; }
}

public sealed class FilePart : Part
{
    public required string Mime { get; set; }
    public string? Filename { get; set; }
    public required string Url { get; set; }
    public FilePartSource? Source { get; set; }
}

public sealed class ToolPart : Part
{
    [JsonPropertyName("callID")] public required string CallId { get; set; }
    public required string Tool { get; set; }
    public required ToolState State { get; set; }
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}

public sealed class StepStartPart : Part
{
    public string? Snapshot { get; set; }
}

public sealed class StepFinishPart : Part
{
    public required string Reason { get; set; }
    public string? Snapshot { get; set; }
    public double Cost { get; set; }
    public required StepFinishTokens Tokens { get; set; }
}

public sealed class StepFinishTokens
{
    public double? Total { get; set; }
    public double Input { get; set; }
    public double Output { get; set; }
    public double Reasoning { get; set; }
    public required TokenCache Cache { get; set; }
}

public sealed class SnapshotPart : Part
{
    public required string Snapshot { get; set; }
}

public sealed class PatchPart : Part
{
    public required string Hash { get; set; }
    public List<string> Files { get; set; } = [];
}

public sealed class AgentPart : Part
{
    public required string Name { get; set; }
    public AgentPartSource? Source { get; set; }
}

public sealed class RetryPart : Part
{
    public double Attempt { get; set; }
    public required AssistantError Error { get; set; }
    public required RetryPartTime Time { get; set; }
}

public sealed class RetryPartTime
{
    public double Created { get; set; }
}

public sealed class CompactionPart : Part
{
    public bool Auto { get; set; }
    public bool? Overflow { get; set; }
    [JsonPropertyName("tail_start_id")] public string? TailStartId { get; set; }
}

public sealed class SubtaskPart : Part
{
    public required string Prompt { get; set; }
    public required string Description { get; set; }
    public required string Agent { get; set; }
    public SubtaskPartModel? Model { get; set; }
    public string? Command { get; set; }
}

public sealed class SubtaskPartModel
{
    [JsonPropertyName("providerID")] public required string ProviderId { get; set; }
    [JsonPropertyName("modelID")] public required string ModelId { get; set; }
}

#endregion

#region ToolState hierarchy

public sealed class ToolStatePending : ToolState
{
    public required string Raw { get; set; }
}

public sealed class ToolStateRunning : ToolState
{
    public string? Title { get; set; }
    public Dictionary<string, JsonElement>? Metadata { get; set; }
    public required ToolTimeRange Time { get; set; }
}

public sealed class ToolStateCompleted : ToolState
{
    public required string Output { get; set; }
    public required string Title { get; set; }
    public Dictionary<string, JsonElement> Metadata { get; set; } = [];
    public required ToolTimeRange Time { get; set; }
    public List<FilePart>? Attachments { get; set; }
}

public sealed class ToolStateError : ToolState
{
    public required string Error { get; set; }
    public Dictionary<string, JsonElement>? Metadata { get; set; }
    public required ToolTimeRange Time { get; set; }
}

#endregion
