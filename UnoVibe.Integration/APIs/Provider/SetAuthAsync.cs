namespace UnoVibe.Integration;

/// <summary>PUT /auth/{providerID} body — stores an API-key credential.</summary>
public sealed class AuthSetRequest
{
    public string Type { get; set; } = "api";
    public required string Key { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Metadata { get; set; }
}

partial class OpencodeClient
{
    /// <summary>
    /// Put /auth/{providerID} — stores an API-key credential. OAuth tokens are written
    /// server-side by the oauth callback endpoint instead.
    /// </summary>
    public Task SetAuthAsync(string providerId, AuthSetRequest request,
        CancellationToken ct = default)
        => PutAsync(
            $"/auth/{Uri.EscapeDataString(providerId)}",
            request,
            AppJsonContext.Default.AuthSetRequest,
            ct
        );
}
