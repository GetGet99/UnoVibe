namespace UnoVibe.Integration;

/// <summary>
/// One message from <c>GET /session/{id}/message</c>. The <c>Info</c> and <c>Parts</c>
/// fields remain as <see cref="JsonElement"/> for now — the full message/part type unions
/// (12 part types, user vs assistant info) will be modeled in a follow-up.
/// </summary>
public sealed class MessageWithParts
{
    public JsonElement? Info { get; set; }

    public List<JsonElement>? Parts { get; set; }
}
