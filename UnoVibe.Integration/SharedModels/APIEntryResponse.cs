namespace UnoVibe.Integration;

/// <summary>
/// Generic wire format of every v2 <c>/api/*</c> GET endpoint: <c>{ location, data }</c>.
/// </summary>
public sealed class APIEntryResponse<T>
{
    public LocationInfo Location { get; set; } = new();
    public required T Data { get; set; }
}
