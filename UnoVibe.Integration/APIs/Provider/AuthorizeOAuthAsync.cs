namespace UnoVibe.Integration;

/// <summary>POST /provider/{providerID}/oauth/authorize body.</summary>
public sealed class OAuthAuthorizeRequest
{
    public required int Method { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Inputs { get; set; }
}

/// <summary>POST /provider/{providerID}/oauth/authorize response.</summary>
public sealed class OAuthAuthorization
{
    public string Url { get; set; } = "";
    /// <summary>"auto" (no code — the callback completes as-is) or "code" (paste the authorization code).</summary>
    public string Method { get; set; } = "";
    public string Instructions { get; set; } = "";
}

partial class OpencodeClient
{
    /// <summary>
    /// Post /provider/{providerID}/oauth/authorize — starts an OAuth flow for the given auth
    /// method index, returning the URL to visit.
    /// </summary>
    public Task<Result<OAuthAuthorization>> AuthorizeOAuthAsync(string providerId, OAuthAuthorizeRequest request,
        CancellationToken ct = default)
        => PostResultAsync(
            $"/provider/{Uri.EscapeDataString(providerId)}/oauth/authorize",
            request, AppJsonContext.Default.OAuthAuthorizeRequest,
            AppJsonContext.Default.OAuthAuthorization,
            ct
        );
}
