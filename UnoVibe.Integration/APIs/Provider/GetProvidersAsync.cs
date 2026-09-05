namespace UnoVibe.Integration;

/// <summary>
/// GET /provider response — <c>{ all, default, connected }</c>. <c>all</c> is the provider
/// catalog (Models.dev merged with runtime providers), <c>connected</c> the provider ids with
/// a stored credential.
/// </summary>
public sealed class ProviderListResult
{
    public List<ProviderInfo>? All { get; set; }
    public Dictionary<string, string>? Default { get; set; }
    public List<string>? Connected { get; set; }
}

/// <summary>One provider entry from <see cref="ProviderListResult.All"/>.</summary>
public sealed class ProviderInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, ProviderModelInfo>? Models { get; set; }
}

/// <summary>One model entry inside a <see cref="ProviderInfo.Models"/> map (keyed by model id).</summary>
public sealed class ProviderModelInfo
{
    public string Name { get; set; } = "";
    public Dictionary<string, object>? Variants { get; set; }
    public ModelLimitInfo? Limit { get; set; }
}

/// <summary>Context-window limit for a model.</summary>
public sealed class ModelLimitInfo
{
    public long Context { get; set; }
}

partial class OpencodeClient
{
    /// <summary>
    /// Get the provider catalog <c>{ all, default, connected }</c>.
    /// </summary>
    public Task<Result<ProviderListResult>> GetProvidersAsync(CancellationToken ct = default)
        => GetResultAsync("/provider", AppJsonContext.Default.ProviderListResult, ct);
}
