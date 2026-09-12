namespace UnoVibe.Integration;

/// <summary>
/// Full session DTO from the opencode REST API and SSE events.
/// Used by <c>GET /session</c>, <c>GET /session/{id}</c>, and
/// <c>session.created/updated/deleted</c> events.
/// </summary>
public sealed class SessionInfo
{
    public required string Id { get; set; }
    public required string Slug { get; set; }
    public required string Title { get; set; }
    [JsonPropertyName("projectID")] public required string ProjectId { get; set; }
    [JsonPropertyName("workspaceID")] public string? WorkspaceId { get; set; }
    public required string Directory { get; set; }
    public string? Path { get; set; }
    [JsonPropertyName("parentID")] public string? ParentId { get; set; }
    public string? Agent { get; set; }
    public SessionModelInfo? Model { get; set; }
    public required string Version { get; set; }
    public Dictionary<string, JsonElement>? Metadata { get; set; }
    public required SessionTimeInfo Time { get; set; }
    public double? Cost { get; set; }
    public SessionTokensInfo? Tokens { get; set; }
    public SessionSummaryInfo? Summary { get; set; }
    public SessionShareInfo? Share { get; set; }
    public List<SessionPermissionRule>? Permission { get; set; }
    public SessionRevertInfo? Revert { get; set; }
}

public sealed class SessionModelInfo
{
    public required string Id { get; set; }
    [JsonPropertyName("providerID")] public required string ProviderId { get; set; }
    public string? Variant { get; set; }
}

public sealed class SessionTimeInfo
{
    public double Created { get; set; }
    public double Updated { get; set; }
    public double? Compacting { get; set; }
    public double? Archived { get; set; }
}

public sealed class SessionTokensInfo
{
    public double Input { get; set; }
    public double Output { get; set; }
    public double Reasoning { get; set; }
    public required SessionCacheInfo Cache { get; set; }
}

public sealed class SessionCacheInfo
{
    public double Read { get; set; }
    public double Write { get; set; }
}

public sealed class SessionSummaryInfo
{
    public double Additions { get; set; }
    public double Deletions { get; set; }
    public double Files { get; set; }
    public List<Events.SnapshotFileDiff>? Diffs { get; set; }
}

public sealed class SessionShareInfo
{
    public required string Url { get; set; }
}

public sealed class SessionPermissionRule
{
    public required string Permission { get; set; }
    public required string Pattern { get; set; }
    public required string Action { get; set; }
}

public sealed class SessionRevertInfo
{
    [JsonPropertyName("messageID")] public required string MessageId { get; set; }
    [JsonPropertyName("partID")] public string? PartId { get; set; }
    public string? Snapshot { get; set; }
    public string? Diff { get; set; }
}
