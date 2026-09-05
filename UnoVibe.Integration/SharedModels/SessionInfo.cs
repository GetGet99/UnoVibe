using System.Text.Json.Serialization;

namespace UnoVibe.Integration;

/// <summary>
/// Plain DTO for a session list item from the opencode REST API.
/// Structure matches the server JSON — the main project flattens these for display.
/// </summary>
public sealed class SessionInfo
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Directory { get; set; } = "";

    [JsonPropertyName("projectID")]
    public string ProjectId { get; set; } = "";

    public string Path { get; set; } = "";
    public string Agent { get; set; } = "";

    [JsonPropertyName("parentID")]
    public string ParentId { get; set; } = "";

    public SessionModelInfo? Model { get; set; }
    public SessionTimeInfo? Time { get; set; }
    public double Cost { get; set; }
    public SessionTokensInfo? Tokens { get; set; }
}

/// <summary>Nested <c>model</c> object in a session.</summary>
public sealed class SessionModelInfo
{
    public string Id { get; set; } = "";

    [JsonPropertyName("providerID")]
    public string ProviderId { get; set; } = "";

    public string Variant { get; set; } = "";
}

/// <summary>Nested <c>time</c> object in a session.</summary>
public sealed class SessionTimeInfo
{
    public long Updated { get; set; }
}

/// <summary>Nested <c>tokens</c> object in a session.</summary>
public sealed class SessionTokensInfo
{
    public long Input { get; set; }
    public long Output { get; set; }
    public long Reasoning { get; set; }
    public SessionCacheInfo? Cache { get; set; }
}

/// <summary>Nested <c>tokens.cache</c> object in a session.</summary>
public sealed class SessionCacheInfo
{
    public long Read { get; set; }
    public long Write { get; set; }
}
