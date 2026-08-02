using System.Net.Http.Json;
using System.Text.Json;

namespace UnoVibe.Services;

/// <summary>
/// Minimal HTTP client for the opencode server REST API.
/// </summary>
public sealed class OpencodeClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public OpencodeClient(string baseUrl)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        Http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
    }

    public string BaseUrl { get; }
    public HttpClient Http { get; }

    public async Task<bool> HealthAsync(CancellationToken ct = default)
    {
        using var response = await Http.GetAsync("/global/health", ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<string?> CreateSessionAsync(string? title = null, CancellationToken ct = default)
    {
        using var response = await Http.PostAsJsonAsync(
            "/session",
            new { title = string.IsNullOrEmpty(title) ? "New Chat" : title },
            Json,
            ct);
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
    }

    public async Task SendPromptAsync(string sessionId, string text, CancellationToken ct = default)
    {
        using var response = await Http.PostAsJsonAsync(
            $"/session/{sessionId}/prompt_async",
            new { parts = new[] { new { type = "text", text } } },
            Json,
            ct);
        response.EnsureSuccessStatusCode();
    }
}
