namespace UnoVibe.Integration;

/// <summary>POST /session body.</summary>
public sealed class CreateSessionRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Agent { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CreateSessionModelRequest? Model { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Variant { get; set; }
}

/// <summary>The <c>model</c> field of <see cref="CreateSessionRequest"/>.</summary>
public sealed class CreateSessionModelRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderID { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Variant { get; set; }
}

partial class OpencodeClient
{
    /// <summary>
    /// Post /session — creates a session. Returns the full <see cref="SessionInfo"/> with
    /// default/initial values (cost=0, zeroed tokens, no summary/share/revert).
    /// </summary>
    public async Task<Result<SessionInfo>> CreateSessionAsync(CreateSessionRequest request, string? directory = null, CancellationToken ct = default)
    {
        var url = string.IsNullOrEmpty(directory) ? "/session" : $"/session?directory={Uri.EscapeDataString(directory)}";
        return await PostResultAsync(url, request, AppJsonContext.Default.CreateSessionRequest, AppJsonContext.Default.SessionInfo, ct);
    }
}
