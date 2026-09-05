namespace UnoVibe.Integration;

/// <summary>POST /permission/{requestID}/reply body.</summary>
public sealed class ReplyPermissionRequest
{
    public required string Reply { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }
}

partial class OpencodeClient
{
    /// <summary>
    /// Post /permission/{requestID}/reply — answers a pending permission request.
    /// <c>reply</c> is "once", "always", or "reject"; an optional message may be sent with a rejection.
    /// </summary>
    public Task ReplyPermissionAsync(string requestId, ReplyPermissionRequest request,
        string? directory = null, CancellationToken ct = default)
        => PostAsync(
            DirectoryUrl($"/permission/{requestId}/reply", directory),
            request, AppJsonContext.Default.ReplyPermissionRequest,
            ct
        );
}
