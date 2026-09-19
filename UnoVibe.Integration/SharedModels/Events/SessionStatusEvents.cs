namespace UnoVibe.Integration.Events;

public sealed class SessionStatusEvent
{
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    public required SessionStatusPayload Status { get; set; }
}

public sealed class SessionIdleEvent
{
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
}

public sealed class SessionErrorEvent
{
    [JsonPropertyName("sessionID")] public string? SessionId { get; set; }
    public AssistantError? Error { get; set; }
}

public sealed class SessionDiffEvent
{
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    public List<SnapshotFileDiff> Diff { get; set; } = [];
}

public sealed class SessionCompactedEvent
{
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
}
