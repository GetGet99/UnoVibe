namespace UnoVibe.Integration.Events;

#region Session CRUD events

/// <summary>
/// Shared payload for <c>session.created</c>, <c>session.updated</c>, and
/// <c>session.deleted</c> — all three carry the same shape.
/// </summary>
public sealed class SessionCrudEvent
{
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    public required SessionInfo Info { get; set; }
}

#endregion
