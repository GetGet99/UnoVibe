namespace UnoVibe.Integration.Events;

#region Supporting types for V2 events

public sealed class ModelRef
{
    [JsonPropertyName("providerID")] public required string ProviderId { get; set; }
    [JsonPropertyName("modelID")] public required string ModelId { get; set; }
}

public sealed class LocationRef
{
    public required string Directory { get; set; }
    [JsonPropertyName("workspaceID")] public string? WorkspaceId { get; set; }
}

public sealed class PromptPayload
{
    public required string Text { get; set; }
    public List<FileAttachmentPayload>? Files { get; set; }
    public List<AgentAttachmentPayload>? Agents { get; set; }
}

public sealed class FileAttachmentPayload
{
    public required string Uri { get; set; }
    public required string Mime { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public PromptSource? Source { get; set; }
}

public sealed class AgentAttachmentPayload
{
    public required string Name { get; set; }
    public PromptSource? Source { get; set; }
}

public sealed class PromptSource
{
    public double Start { get; set; }
    public double End { get; set; }
    public required string Text { get; set; }
}

public sealed class UnknownError
{
    public string Type { get; set; } = "unknown";
    public required string Message { get; set; }
}

public sealed class RetryError
{
    public required string Message { get; set; }
    public double? StatusCode { get; set; }
    public bool IsRetryable { get; set; }
    public Dictionary<string, string>? ResponseHeaders { get; set; }
    public string? ResponseBody { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

public sealed class RevertState
{
    [JsonPropertyName("messageID")] public required string MessageId { get; set; }
    [JsonPropertyName("partID")] public string? PartId { get; set; }
    public string? Snapshot { get; set; }
    public string? Diff { get; set; }
    public List<RevertFileDiff>? Files { get; set; }
}

public sealed class RevertFileDiff
{
    public required string Path { get; set; }
    public FileDiffStatus Status { get; set; }
    public double Additions { get; set; }
    public double Deletions { get; set; }
    public required string Patch { get; set; }
}

public sealed class ToolProviderInfo
{
    public bool Executed { get; set; }
    public Dictionary<string, Dictionary<string, JsonElement>>? Metadata { get; set; }
}

#endregion

#region Agent / Model switching

public sealed class AgentSwitchedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("messageID")] public required string MessageId { get; set; }
    public required string Agent { get; set; }
}

public sealed class ModelSwitchedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("messageID")] public required string MessageId { get; set; }
    public required ModelRef Model { get; set; }
}

public sealed class MovedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    public required LocationRef Location { get; set; }
    public string? Subdirectory { get; set; }
}

#endregion

#region Prompting

public sealed class PromptedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("messageID")] public required string MessageId { get; set; }
    public required PromptPayload Prompt { get; set; }
    public required string Delivery { get; set; }
}

public sealed class PromptAdmittedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("messageID")] public required string MessageId { get; set; }
    public required PromptPayload Prompt { get; set; }
    public required string Delivery { get; set; }
}

public sealed class ContextUpdatedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("messageID")] public required string MessageId { get; set; }
    public required string Text { get; set; }
}

public sealed class SyntheticEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("messageID")] public required string MessageId { get; set; }
    public required string Text { get; set; }
}

#endregion

#region Shell

public sealed class ShellStartedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("messageID")] public required string MessageId { get; set; }
    [JsonPropertyName("callID")] public required string CallId { get; set; }
    public required string Command { get; set; }
}

public sealed class ShellEndedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("callID")] public required string CallId { get; set; }
    public required string Output { get; set; }
}

#endregion

#region Step

public sealed class StepStartedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("assistantMessageID")] public required string AssistantMessageId { get; set; }
    public required string Agent { get; set; }
    public required ModelRef Model { get; set; }
    public string? Snapshot { get; set; }
}

public sealed class StepEndedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("assistantMessageID")] public required string AssistantMessageId { get; set; }
    public required string Finish { get; set; }
    public double Cost { get; set; }
    public required StepEndTokens Tokens { get; set; }
    public string? Snapshot { get; set; }
    public List<string>? Files { get; set; }
}

public sealed class StepEndTokens
{
    public double Input { get; set; }
    public double Output { get; set; }
    public double Reasoning { get; set; }
    public required TokenCache Cache { get; set; }
}

public sealed class StepFailedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("assistantMessageID")] public required string AssistantMessageId { get; set; }
    public required UnknownError Error { get; set; }
}

#endregion

#region Text streaming

public sealed class TextStartedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("assistantMessageID")] public required string AssistantMessageId { get; set; }
    [JsonPropertyName("textID")] public required string TextId { get; set; }
}

public sealed class TextDeltaEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("assistantMessageID")] public required string AssistantMessageId { get; set; }
    [JsonPropertyName("textID")] public required string TextId { get; set; }
    public required string Delta { get; set; }
}

public sealed class TextEndedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("assistantMessageID")] public required string AssistantMessageId { get; set; }
    [JsonPropertyName("textID")] public required string TextId { get; set; }
    public required string Text { get; set; }
}

#endregion

#region Reasoning

public sealed class ReasoningStartedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("assistantMessageID")] public required string AssistantMessageId { get; set; }
    [JsonPropertyName("reasoningID")] public required string ReasoningId { get; set; }
    public Dictionary<string, Dictionary<string, JsonElement>>? ProviderMetadata { get; set; }
}

public sealed class ReasoningDeltaEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("assistantMessageID")] public required string AssistantMessageId { get; set; }
    [JsonPropertyName("reasoningID")] public required string ReasoningId { get; set; }
    public required string Delta { get; set; }
}

public sealed class ReasoningEndedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("assistantMessageID")] public required string AssistantMessageId { get; set; }
    [JsonPropertyName("reasoningID")] public required string ReasoningId { get; set; }
    public required string Text { get; set; }
    public Dictionary<string, Dictionary<string, JsonElement>>? ProviderMetadata { get; set; }
}

#endregion

#region Tool input

public sealed class ToolInputStartedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("assistantMessageID")] public required string AssistantMessageId { get; set; }
    [JsonPropertyName("callID")] public required string CallId { get; set; }
    public required string Name { get; set; }
}

public sealed class ToolInputDeltaEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("assistantMessageID")] public required string AssistantMessageId { get; set; }
    [JsonPropertyName("callID")] public required string CallId { get; set; }
    public required string Delta { get; set; }
}

public sealed class ToolInputEndedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("assistantMessageID")] public required string AssistantMessageId { get; set; }
    [JsonPropertyName("callID")] public required string CallId { get; set; }
    public required string Text { get; set; }
}

#endregion

#region Tool lifecycle

public sealed class ToolCalledEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("assistantMessageID")] public required string AssistantMessageId { get; set; }
    [JsonPropertyName("callID")] public required string CallId { get; set; }
    public required string Tool { get; set; }
    public JsonElement Input { get; set; }
    public required ToolProviderInfo Provider { get; set; }
}

public sealed class ToolProgressEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("assistantMessageID")] public required string AssistantMessageId { get; set; }
    [JsonPropertyName("callID")] public required string CallId { get; set; }
    public JsonElement Structured { get; set; }
    public List<ToolContent> Content { get; set; } = [];
}

public sealed class ToolSuccessEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("assistantMessageID")] public required string AssistantMessageId { get; set; }
    [JsonPropertyName("callID")] public required string CallId { get; set; }
    public JsonElement Structured { get; set; }
    public List<ToolContent> Content { get; set; } = [];
    public List<string>? OutputPaths { get; set; }
    public JsonElement? Result { get; set; }
    public required ToolProviderInfo Provider { get; set; }
}

public sealed class ToolFailedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("assistantMessageID")] public required string AssistantMessageId { get; set; }
    [JsonPropertyName("callID")] public required string CallId { get; set; }
    public required UnknownError Error { get; set; }
    public JsonElement? Result { get; set; }
    public required ToolProviderInfo Provider { get; set; }
}

#endregion

#region Retry

public sealed class RetriedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    public double Attempt { get; set; }
    public required RetryError Error { get; set; }
}

#endregion

#region Compaction

public sealed class CompactionStartedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("messageID")] public required string MessageId { get; set; }
    public CompactionReason Reason { get; set; }
}

public sealed class CompactionDeltaEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("messageID")] public required string MessageId { get; set; }
    public required string Text { get; set; }
}

public sealed class CompactionEndedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("messageID")] public required string MessageId { get; set; }
    public CompactionReason Reason { get; set; }
    public required string Text { get; set; }
    public required string Recent { get; set; }
}

#endregion

#region Revert

public sealed class RevertStagedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    public required RevertState Revert { get; set; }
}

public sealed class RevertClearedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
}

public sealed class RevertCommittedEvent
{
    public double Timestamp { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("messageID")] public required string MessageId { get; set; }
}

#endregion
