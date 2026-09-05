namespace UnoVibe.Integration;

/// <summary>
/// Plain DTO for a pending permission request from <c>GET /permission</c>.
/// </summary>
public sealed class PermissionRequestItem
{
    public string Id { get; set; } = "";

    [JsonPropertyName("sessionID")]
    public string SessionId { get; set; } = "";

    public string Permission { get; set; } = "";
    public string[] Patterns { get; set; } = [];
    public string[] Always { get; set; } = [];
    public PermissionToolInfo? Tool { get; set; }
    public JsonElement Metadata { get; set; }
}

/// <summary>Nested <c>tool</c> object inside a permission request.</summary>
public sealed class PermissionToolInfo
{
    [JsonPropertyName("messageID")]
    public string MessageId { get; set; } = "";

    [JsonPropertyName("callID")]
    public string CallId { get; set; } = "";
}

partial class OpencodeClient
{
    /// <summary>
    /// Lists pending permission requests for the workspace directory.
    /// </summary>
    public Task<Result<List<PermissionRequestItem>>> GetPendingPermissionsAsync(string? directory = null,
        CancellationToken ct = default)
        => GetResultAsync(DirectoryUrl("/permission", directory), AppJsonContext.Default.ListPermissionRequestItem, ct);
}
