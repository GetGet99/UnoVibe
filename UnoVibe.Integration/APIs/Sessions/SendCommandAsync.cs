namespace UnoVibe.Integration;

/// <summary>POST /session/{id}/command body — invokes a server-side custom command.</summary>
public sealed class SendCommandRequest
{
    public required string Command { get; set; }
    public required string Arguments { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Agent { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Variant { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PromptPart>? Parts { get; set; }
}

partial class OpencodeClient
{
    /// <summary>
    /// Post /session/{id}/command — invokes a server-side custom command. The server expands
    /// the command's template and runs the turn; this request blocks until that turn completes,
    /// so a dedicated client without a timeout runs it.
    /// </summary>
    public async Task SendCommandAsync(
        string sessionId, SendCommandRequest request,
        CancellationToken ct = default)
    {
        using var commandHttp = new HttpClient
        {
            BaseAddress = Http.BaseAddress,
            Timeout = Timeout.InfiniteTimeSpan,
        };
        if (Http.DefaultRequestHeaders.Authorization is { } auth)
            commandHttp.DefaultRequestHeaders.Authorization = auth;

        using var response = await commandHttp.PostAsJsonAsync(
            $"/session/{sessionId}/command", request, AppJsonContext.Default.SendCommandRequest, ct);
        response.EnsureSuccessStatusCode();
    }
}
