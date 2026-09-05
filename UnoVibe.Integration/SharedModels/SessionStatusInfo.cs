namespace UnoVibe.Integration;

/// <summary>
/// Per-session status from <c>GET /session/status</c>. Discriminated union on <see cref="Type"/>:
/// <c>"idle"</c>, <c>"busy"</c>, or <c>"retry"</c> (with attempt/message/next fields).
/// </summary>
public sealed class SessionStatusInfo
{
    public string Type { get; set; } = "";

    public long Attempt { get; set; }

    public string Message { get; set; } = "";

    public long Next { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionStatusActionInfo? Action { get; set; }
}

/// <summary>Optional action details inside a <c>"retry"</c> status.</summary>
public sealed class SessionStatusActionInfo
{
    public string Reason { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Label { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Link { get; set; }
}
