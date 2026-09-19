namespace UnoVibe.Integration;

/// <summary>
/// Plain DTO for a pending permission request from <c>GET /permission</c>.
/// </summary>
public sealed class PermissionRequestDto
{
    public string Id { get; set; } = "";

    [JsonPropertyName("sessionID")]
    public string SessionId { get; set; } = "";

    public string Permission { get; set; } = "";
    public string[] Patterns { get; set; } = [];
    public string[] Always { get; set; } = [];
    public PermissionRequestToolInfo? Tool { get; set; }
    public JsonElement Metadata { get; set; }
}

/// <summary>Nested <c>tool</c> object inside a permission request.</summary>
public sealed class PermissionRequestToolInfo
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
    public Task<Result<List<PermissionRequestDto>>> GetPendingPermissionsAsync(string? directory = null,
        CancellationToken ct = default)
        => GetResultAsync(DirectoryUrl("/permission", directory), AppJsonContext.Default.ListPermissionRequestDto, ct);
}
