namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>
    /// Post /mcp/{name}/auth/authenticate — starts the MCP server's OAuth flow and waits
    /// for it to complete. Uses a dedicated client with a 6-minute timeout.
    /// </summary>
    public async Task<Result<McpStatusInfo>> McpAuthenticateAsync(string name, string? directory = null,
        CancellationToken ct = default)
    {
        using var authHttp = new HttpClient
        {
            BaseAddress = Http.BaseAddress,
            Timeout = TimeSpan.FromMinutes(6),
        };
        if (Http.DefaultRequestHeaders.Authorization is { } auth)
            authHttp.DefaultRequestHeaders.Authorization = auth;

        try
        {
            using var response = await authHttp.PostAsync(
                DirectoryUrl($"/mcp/{Uri.EscapeDataString(name)}/auth/authenticate", directory), null, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                return Result<McpStatusInfo>.Failure(ApiError.Http((int)response.StatusCode, body));
            }
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            var value = await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.McpStatusInfo, ct);
            return value is not null
                ? Result<McpStatusInfo>.Success(value)
                : Result<McpStatusInfo>.Failure(ApiError.Http(0, "Empty response"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Result<McpStatusInfo>.Failure(ApiError.Network(ex.Message));
        }
    }
}
