namespace UnoVibe.Integration;

/// <summary>
/// One auth method for a provider (GET /provider/auth): either an API-key entry
/// (<c>type == "api"</c>) or an OAuth flow (<c>type == "oauth"</c>). Optional prompts
/// (text/select inputs) must be answered before completing the method.
/// </summary>
public sealed class ProviderAuthMethod
{
    public string Type { get; set; } = "";
    public string Label { get; set; } = "";
    public List<AuthPrompt>? Prompts { get; set; }
}

/// <summary>A text or select input the auth method wants answered.</summary>
public sealed class AuthPrompt
{
    public string Type { get; set; } = "";
    public string Key { get; set; } = "";
    public string Message { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Placeholder { get; set; }

    public List<AuthPromptOption>? Options { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AuthWhen? When { get; set; }
}

/// <summary>An option of a select <see cref="AuthPrompt"/>.</summary>
public sealed class AuthPromptOption
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hint { get; set; }
}

/// <summary>Condition gating an <see cref="AuthPrompt"/> on an earlier prompt's value.</summary>
public sealed class AuthWhen
{
    public string Key { get; set; } = "";
    public string Op { get; set; } = "";
    public string Value { get; set; } = "";
}

partial class OpencodeClient
{
    /// <summary>
    /// Get /provider/auth — the auth methods per provider. Each provider maps to an array
    /// of <see cref="ProviderAuthMethod"/> with full type, label, prompts, options, and when fields.
    /// </summary>
    public Task<Result<Dictionary<string, ProviderAuthMethod[]>>> GetProviderAuthMethodsAsync(
        CancellationToken ct = default)
        => GetResultAsync("/provider/auth", AppJsonContext.Default.DictionaryStringProviderAuthMethodArray, ct);
}
