namespace UnoVibe.Integration;

/// <summary>POST /provider/{providerID}/oauth/callback body (code optional for "auto" methods).</summary>
public sealed class OAuthCallbackRequest
{
    public required int Method { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; set; }
}

partial class OpencodeClient
{
    /// <summary>
    /// Post /provider/{providerID}/oauth/callback — completes an OAuth flow that was started
    /// with <see cref="AuthorizeOAuthAsync"/>. Writes the credential server-side.
    /// </summary>
    public Task CompleteOAuthAsync(string providerId, OAuthCallbackRequest request,
        CancellationToken ct = default)
        => PostAsync(
            $"/provider/{Uri.EscapeDataString(providerId)}/oauth/callback",
            request, AppJsonContext.Default.OAuthCallbackRequest, ct);
}
