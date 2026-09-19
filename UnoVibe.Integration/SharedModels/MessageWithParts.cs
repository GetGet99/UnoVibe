using UnoVibe.Integration.Events;

namespace UnoVibe.Integration;

/// <summary>
/// One message from <c>GET /session/{id}/message</c>. Contains the full typed message
/// info (user or assistant) and all part types.
/// </summary>
public sealed class MessageWithParts
{
    public MessageInfo? Info { get; set; }

    public List<Part>? Parts { get; set; }
}
