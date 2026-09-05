using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace UnoVibe.Integration;

/// <summary>
/// Minimal HTTP client for the opencode server REST API.
/// Endpoint methods are defined in separate partial class files (one per endpoint).
/// </summary>
public sealed partial class OpencodeClient
{
    /// <summary>Environment variable holding the server password (Basic auth).</summary>
    public const string PasswordEnvVar = "OPENCODE_SERVER_PASSWORD";

    /// <summary>Environment variable holding the server username (defaults to "opencode").</summary>
    public const string UsernameEnvVar = "OPENCODE_SERVER_USERNAME";

    public OpencodeClient(string baseUrl, string? password = null, string? username = null)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        Http = new HttpClient { BaseAddress = new Uri(BaseUrl) };

        password ??= Environment.GetEnvironmentVariable(PasswordEnvVar);
        if (!string.IsNullOrEmpty(password))
        {
            var user = !string.IsNullOrEmpty(username)
                ? username
                : Environment.GetEnvironmentVariable(UsernameEnvVar) ?? "opencode";
            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));
        }
    }

    string BaseUrl { get; }
    HttpClient Http { get; }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>Appends the ?directory= query used by instance-scoped routes.</summary>
    internal static string DirectoryUrl(string path, string? directory) =>
        string.IsNullOrEmpty(directory)
            ? path
            : $"{path}?directory={Uri.EscapeDataString(directory)}";

    /// <summary>
    /// Builds the deep-object location query for the /api/* endpoints.
    /// Serializes as <c>location[directory]=...</c>.
    /// </summary>
    internal static string LocationUrl(string path, string? directory) =>
        string.IsNullOrEmpty(directory)
            ? path
            : $"{path}?location%5Bdirectory%5D={Uri.EscapeDataString(directory)}";

    // ── Result helpers (non-throwing) ─────────────────────────────────────────

    private async Task<Result<T>> GetResultAsync<T>(string url, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        try
        {
            using var response = await Http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return Result<T>.Failure(ApiError.Http(response.StatusCode, response.ReasonPhrase ?? "Error"));
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            var value = await JsonSerializer.DeserializeAsync(stream, typeInfo, ct);
            return value is not null
                ? Result<T>.Success(value)
                : Result<T>.Failure(ApiError.Http(0, "Empty response"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Result<T>.Failure(ApiError.Network(ex.Message));
        }
    }

    private async Task<Result<TOut>> PostResultAsync<TIn, TOut>(
        string url, TIn input, JsonTypeInfo<TIn> inputTypeInfo, JsonTypeInfo<TOut> outputTypeInfo, CancellationToken ct)
    {
        try
        {
            using var response = await Http.PostAsJsonAsync(url, input, inputTypeInfo, ct);
            if (!response.IsSuccessStatusCode)
                return Result<TOut>.Failure(ApiError.Http(response.StatusCode, response.ReasonPhrase ?? "Error"));
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            var value = await JsonSerializer.DeserializeAsync(stream, outputTypeInfo, ct);
            return value is not null
                ? Result<TOut>.Success(value)
                : Result<TOut>.Failure(ApiError.Http(0, "Empty response"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Result<TOut>.Failure(ApiError.Network(ex.Message));
        }
    }

    // ── Throwing helpers ─────────────────────────────────────────────────────

    private async Task<T> GetAsync<T>(string url, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync(stream, typeInfo, ct) ?? throw new NullReferenceException();
    }

    private Task PostAsync(string url, CancellationToken ct)
        => PostAsync(url, new EmptyRequest(), AppJsonContext.Default.EmptyRequest, ct);

    private async Task PutAsync<T>(string url, T input, JsonTypeInfo<T> inputTypeInfo, CancellationToken ct)
    {
        using var response = await Http.PutAsJsonAsync(url, input, inputTypeInfo, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task PostAsync<T>(string url, T input, JsonTypeInfo<T> inputTypeInfo, CancellationToken ct)
    {
        using var response = await Http.PostAsJsonAsync(url, input, inputTypeInfo, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<TOut> PostAsync<TIn, TOut>(string url, TIn input, JsonTypeInfo<TIn> inputTypeInfo, JsonTypeInfo<TOut> outputTypeInfo, CancellationToken ct)
    {
        using var response = await Http.PostAsJsonAsync(url, input, inputTypeInfo, ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync(stream, outputTypeInfo, ct) ?? throw new NullReferenceException();
    }

    private async Task PatchAsync<T>(string url, T input, JsonTypeInfo<T> inputTypeInfo, CancellationToken ct)
    {
        using var response = await Http.PatchAsJsonAsync(url, input, inputTypeInfo, ct);
        response.EnsureSuccessStatusCode();
    }
}
