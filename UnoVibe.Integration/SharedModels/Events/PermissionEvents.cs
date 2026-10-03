namespace UnoVibe.Integration.Events;

#region Permission V1

public sealed class PermissionAskedEvent
{
    public required string Id { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    public required string Permission { get; set; }
    public List<string> Patterns { get; set; } = [];
    public JsonElement Metadata { get; set; }
    public List<string> Always { get; set; } = [];
    public PermissionEventTool? Tool { get; set; }
}

public sealed class PermissionRepliedEvent
{
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("requestID")] public required string RequestId { get; set; }
    public PermissionReply Reply { get; set; }
}

public sealed class PermissionEventTool
{
    [JsonPropertyName("messageID")] public required string MessageId { get; set; }
    [JsonPropertyName("callID")] public required string CallId { get; set; }
}

#endregion

#region Permission V2

public sealed class PermissionV2AskedEvent
{
    public required string Id { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    public required string Action { get; set; }
    public List<string> Resources { get; set; } = [];
    public List<string>? Save { get; set; }
    public JsonElement? Metadata { get; set; }
    public PermissionV2Source? Source { get; set; }
}

public sealed class PermissionV2RepliedEvent
{
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("requestID")] public required string RequestId { get; set; }
    public PermissionReply Reply { get; set; }
}

public sealed class PermissionV2Source
{
    public string Type { get; set; } = "tool";
    [JsonPropertyName("messageID")] public required string MessageId { get; set; }
    [JsonPropertyName("callID")] public required string CallId { get; set; }
}

#endregion
