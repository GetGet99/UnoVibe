namespace UnoVibe.Models.Startup;

/// <summary>
/// Parsed command-line launch target. The app accepts a single positional argument — a
/// folder path or an http(s) server URL — in the spirit of <c>code path/to/folder</c>.
/// </summary>
public sealed record StartupArgs
{
    public required LaunchKind Kind { get; init; }

    /// <summary>The folder path or server URL, depending on <see cref="Kind"/>.</summary>
    public required string StartParam { get; init; }

    public required string? Password { get; init; }

    public static StartupArgs None => new()
    {
        Kind = LaunchKind.None,
        StartParam = "",
        Password = null
    };
}
