using UnoVibe.Models.Startup;

namespace UnoVibe.Helpers;

static class CLIHelper
{
    /// <summary>Parses the process command line into a launch target.</summary>
    public static StartupArgs Parse(string[] args)
    {
        string? positional = null;
        string? password = null;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--password")
            {
                // The next token is the value unless it's absent or another flag. A bare
                // `--password` (or one followed by another option) means "use the env var".
                if (i + 1 < args.Length && !IsFlag(args[i + 1]))
                {
                    password = args[++i];
                }
                else
                {
                    password = Environment.GetEnvironmentVariable(OpencodeHelper.PasswordEnvVar);
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
            return StartupArgs.None;
        if (positional.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || positional.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return new StartupArgs
            {
                Kind = LaunchKind.Server,
                StartParam = positional,
                Password = password,
            };
        // folder

        var fullPath = Path.GetFullPath(positional);
        if (File.Exists(fullPath))
            FailLaunch($"'{positional}' is a file, not a folder.");
        if (!Directory.Exists(fullPath)) Directory.CreateDirectory(fullPath);
        return new StartupArgs
        {
            Kind = LaunchKind.Folder,
            StartParam = fullPath,
            Password = password,
        };
    }

    /// <summary>Terminates the app with a console error, mirroring a CLI launch failure.</summary>
    private static void FailLaunch(string message)
    {
        Console.Error.WriteLine($"UnoVibe: {message}");
        Console.Error.WriteLine("Usage: UnoVibe [folder-or-http-url] [--password [password]]");
        Environment.Exit(1);
    }

    private static bool IsFlag(string s) => s.StartsWith('-');
}