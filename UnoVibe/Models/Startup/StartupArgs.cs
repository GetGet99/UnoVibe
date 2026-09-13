namespace UnoVibe.Models.Startup;

/// <summary>
/// Parsed command-line launch target. The app accepts a single positional argument — a
/// folder path or an http(s) server URL — in the spirit of <c>code path/to/folder</c>.
/// </summary>
public sealed record StartupArgs
{
    public LaunchKind Kind { get; init; } = LaunchKind.None;

    /// <summary>The folder path or server URL, depending on <see cref="Kind"/>.</summary>
    public string Value { get; init; } = "";

    public PasswordMode PasswordMode { get; init; } = PasswordMode.Omitted;

    public string PasswordValue { get; init; } = "";

    /// <summary>
    /// Resolves the password for a <see cref="LaunchKind.Folder"/> launch:
    /// Omitted → null (ServeProcess generates a strong password); FromEnv → the env var
    /// value ("" when unset → unsecured); Provided → the given value.
    /// </summary>
    public string? ResolveFolderPassword() => PasswordMode switch
    {
        PasswordMode.FromEnv => Environment.GetEnvironmentVariable(OpencodeConnection.PasswordEnvVar) ?? "",
        PasswordMode.Provided => PasswordValue,
        _ => null,
    };

    /// <summary>
    /// Resolves the password for a <see cref="LaunchKind.Server"/> launch:
    /// Omitted and Provided("") → "" (no auth, the env var is ignored);
    /// FromEnv → null (the client falls back to the env var itself);
    /// Provided(value) → the given value.
    /// </summary>
    public string? ResolveServerPassword() => PasswordMode switch
    {
        PasswordMode.FromEnv => null,
        PasswordMode.Provided => PasswordValue,
        _ => "",
    };

    /// <summary>Parses the process command line into a launch target.</summary>
    public static StartupArgs Parse()
    {
        var args = Environment.GetCommandLineArgs();
        string? positional = null;
        var mode = PasswordMode.Omitted;
        var value = "";

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--password")
            {
                // The next token is the value unless it's absent or another flag. A bare
                // `--password` (or one followed by another option) means "use the env var".
                if (i + 1 < args.Length && !IsFlag(args[i + 1]))
                {
                    value = args[++i];
                    mode = PasswordMode.Provided;
                }
                else
                {
                    mode = PasswordMode.FromEnv;
                }
            }
            else if (IsFlag(arg))
            {
                // Unknown option — ignored for forward compatibility.
            }
            else if (positional is null)
            {
                positional = arg;
            }
        }

        if (positional is null)
            return new StartupArgs();
        if (positional.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || positional.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return new StartupArgs
            {
                Kind = LaunchKind.Server,
                Value = positional,
                PasswordMode = mode,
                PasswordValue = value,
            };
        return new StartupArgs
        {
            Kind = LaunchKind.Folder,
            Value = positional,
            PasswordMode = mode,
            PasswordValue = value,
        };
    }

    private static bool IsFlag(string s) => s.Length > 1 && s[0] == '-';
}
