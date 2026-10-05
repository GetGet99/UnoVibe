namespace UnoVibe.Models.Startup;

public sealed record StartupArgs
{
    public required LaunchKind Kind { get; init; }

    public required string StartParam { get; init; }

    public required string? Password { get; init; }

    public static StartupArgs None => new()
    {
        Kind = LaunchKind.None,
        StartParam = "",
        Password = null
    };
}
