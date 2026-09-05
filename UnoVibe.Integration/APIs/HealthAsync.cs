namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>
    /// Get /global/health — returns true when the server is healthy.
    /// </summary>
    public async Task<Result<bool>> HealthAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await Http.GetAsync("/global/health", ct);
            return response.IsSuccessStatusCode
                ? Result<bool>.Success(true)
                : Result<bool>.Failure(ApiError.Http(response.StatusCode, response.ReasonPhrase ?? "Unhealthy"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Result<bool>.Failure(ApiError.Network(ex.Message));
        }
    }
}
