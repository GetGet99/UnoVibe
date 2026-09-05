namespace UnoVibe.Integration;

/// <summary>POST /session/{id}/shell body — runs a shell command in the session's directory.</summary>
public sealed class SendShellRequest
{
    public required string Command { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Agent { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SendPromptModelRequest? Model { get; set; }
}

partial class OpencodeClient
{
    /// <summary>
    /// Post /session/{id}/shell — runs a shell command in the session's directory. The server
    /// records it as a user message plus an assistant message with a running bash tool part,
    /// streams output over SSE, and returns once the command exits. Uses a dedicated client
    /// with no timeout.
    /// </summary>
    public async Task SendShellAsync(string sessionId, SendShellRequest request,
        CancellationToken ct = default)
    {
        using var shellHttp = new HttpClient
        {
            BaseAddress = Http.BaseAddress,
            Timeout = Timeout.InfiniteTimeSpan,
        };
        if (Http.DefaultRequestHeaders.Authorization is { } auth)
            shellHttp.DefaultRequestHeaders.Authorization = auth;

        using var response = await shellHttp.PostAsJsonAsync(
            $"/session/{sessionId}/shell", request, AppJsonContext.Default.SendShellRequest, ct);
        response.EnsureSuccessStatusCode();
    }
}
