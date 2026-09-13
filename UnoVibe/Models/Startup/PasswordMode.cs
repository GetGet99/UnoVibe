namespace UnoVibe.Models.Startup;

/// <summary>How the server password is resolved (the <c>--password</c> argument).</summary>
public enum PasswordMode
{
    /// <summary>No <c>--password</c> flag: folder → generate a strong password; server → no password.</summary>
    Omitted,

    /// <summary>Bare <c>--password</c> (no value): take the password from the OPENCODE_SERVER_PASSWORD environment variable.</summary>
    FromEnv,

    /// <summary><c>--password &lt;value&gt;</c>: use the given value (an empty string means no password).</summary>
    Provided,
}
