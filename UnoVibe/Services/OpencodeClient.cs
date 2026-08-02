using System.Net.Http.Json;
using System.Text.Json;
using UnoVibe.Models;

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

    public async Task<string?> CreateSessionAsync(string? title = null, string? directory = null, CancellationToken ct = default)
    {
        var url = string.IsNullOrEmpty(directory) ? "/session" : $"/session?directory={Uri.EscapeDataString(directory)}";
        using var response = await Http.PostAsJsonAsync(
            url,
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

    public async Task<List<SessionInfo>> ListSessionsAsync(CancellationToken ct = default)
    {
        using var response = await Http.GetAsync("/session", ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var list = new List<SessionInfo>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var info = new SessionInfo
            {
                Id = item.GetStringProperty("id"),
                Title = item.GetStringProperty("title"),
                Directory = item.GetStringProperty("directory"),
                ProjectId = item.GetStringProperty("projectID"),
                Path = item.GetStringProperty("path"),
            };
            if (item.TryGetProperty("time", out var time))
                info.Updated = time.TryGetProperty("updated", out var updated) ? updated.GetInt64() : 0;
            if (info.Id.Length > 0) list.Add(info);
        }
        return list;
    }

    public async Task<JsonElement> GetMessagesAsync(string sessionId, CancellationToken ct = default)
    {
        using var response = await Http.GetAsync($"/session/{sessionId}/message", ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }
}

file static class OpencodeClientExtensions
{
    public static string GetStringProperty(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var prop) ? prop.GetString() ?? "" : "";
}
