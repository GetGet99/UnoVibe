using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using UnoVibe.Models;

namespace UnoVibe.Services;

/// <summary>
/// Minimal HTTP client for the opencode server REST API.
/// </summary>
public sealed class OpencodeClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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

    public string BaseUrl { get; }
    public HttpClient Http { get; }

    /// <summary>Last response status code from <see cref="HealthAsync"/>.</summary>
    public System.Net.HttpStatusCode LastHealthStatus { get; private set; }

    public async Task<bool> HealthAsync(CancellationToken ct = default)
    {
        using var response = await Http.GetAsync("/global/health", ct);
        LastHealthStatus = response.StatusCode;
        return response.IsSuccessStatusCode;
    }

    public async Task<string?> CreateSessionAsync(string? title = null, string? directory = null,
        string? agent = null, string? providerId = null, string? modelId = null, string? variant = null,
        CancellationToken ct = default)
    {
        var url = string.IsNullOrEmpty(directory) ? "/session" : $"/session?directory={Uri.EscapeDataString(directory)}";
        var body = new Dictionary<string, object?>
        {
            ["title"] = string.IsNullOrEmpty(title) ? "New Chat" : title,
        };
        if (!string.IsNullOrEmpty(agent)) body["agent"] = agent;
        if (!string.IsNullOrEmpty(providerId) && !string.IsNullOrEmpty(modelId))
        {
            var model = new Dictionary<string, object?>
            {
                ["id"] = modelId,
                ["providerID"] = providerId,
            };
            if (!string.IsNullOrEmpty(variant) && variant != "Default") model["variant"] = variant;
            body["model"] = model;
        }
        using var response = await Http.PostAsJsonAsync(url, body, Json, ct);
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
    }

    public async Task SendPromptAsync(string sessionId, string text,
        string? agent = null, string? providerId = null, string? modelId = null, string? variant = null,
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["parts"] = new[] { new { type = "text", text } },
        };
        if (!string.IsNullOrEmpty(agent)) body["agent"] = agent;
        if (!string.IsNullOrEmpty(providerId) && !string.IsNullOrEmpty(modelId))
            body["model"] = new { providerID = providerId, modelID = modelId };
        if (!string.IsNullOrEmpty(variant) && variant != "Default") body["variant"] = variant;
        using var response = await Http.PostAsJsonAsync($"/session/{sessionId}/prompt_async", body, Json, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task ReplyQuestionAsync(string requestId, IReadOnlyList<IReadOnlyList<string>> answers,
        CancellationToken ct = default)
    {
        var body = new { answers };
        using var response = await Http.PostAsJsonAsync($"/question/{requestId}/reply", body, Json, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<string>> GetModesAsync(CancellationToken ct = default)
    {
        using var response = await Http.GetAsync("/agent", ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var list = new List<string>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var agent in doc.RootElement.EnumerateArray())
        {
            if (agent.GetStringProperty("mode") != "primary") continue;
            var hidden = false;
            if (agent.TryGetProperty("hidden", out var h))
                hidden = h.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.String => h.GetString() == "true",
                    _ => false,
                };
            if (hidden) continue;
            var name = agent.GetStringProperty("name");
            if (name.Length > 0 && !list.Contains(name)) list.Add(name);
        }
        return list;
    }

    public async Task<List<ModelOption>> GetModelsAsync(CancellationToken ct = default)
    {
        using var response = await Http.GetAsync("/provider", ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var list = new List<ModelOption>();

        if (!doc.RootElement.TryGetProperty("connected", out var connected)) return list;
        var connectedIds = new HashSet<string>();
        if (connected.ValueKind == JsonValueKind.Array)
            foreach (var c in connected.EnumerateArray()) connectedIds.Add(c.GetString() ?? "");

        if (!doc.RootElement.TryGetProperty("all", out var all) || all.ValueKind != JsonValueKind.Array) return list;
        foreach (var provider in all.EnumerateArray())
        {
            var providerId = provider.GetStringProperty("id");
            if (connectedIds.Count > 0 && !connectedIds.Contains(providerId)) continue;
            if (!provider.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Object) continue;
            foreach (var kv in models.EnumerateObject())
            {
                var model = kv.Value;
                var variants = new List<string>();
                if (model.TryGetProperty("variants", out var v) && v.ValueKind == JsonValueKind.Object)
                    foreach (var vk in v.EnumerateObject()) variants.Add(vk.Name);
                var name = model.GetStringProperty("name");
                list.Add(new ModelOption
                {
                    ProviderId = providerId,
                    Id = kv.Name,
                    Name = name.Length > 0 ? name : kv.Name,
                    Variants = variants.ToArray(),
                    LimitContext = model.TryGetProperty("limit", out var limit)
                        ? limit.GetInt64Property("context")
                        : 0,
                });
            }
        }
        return list;
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
                Agent = item.GetStringProperty("agent"),
            };
            if (item.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.Object)
            {
                info.ModelId = model.GetStringProperty("id");
                info.ModelProviderId = model.GetStringProperty("providerID");
                info.ModelVariant = model.GetStringProperty("variant");
            }
            if (item.TryGetProperty("time", out var time))
                info.Updated = time.TryGetProperty("updated", out var updated) ? updated.GetInt64() : 0;
            if (item.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Number)
                info.Cost = cost.GetDouble();
            if (item.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object)
            {
                info.TokensInput = tokens.GetInt64Property("input");
                info.TokensOutput = tokens.GetInt64Property("output");
                info.TokensReasoning = tokens.GetInt64Property("reasoning");
                if (tokens.TryGetProperty("cache", out var cache) && cache.ValueKind == JsonValueKind.Object)
                {
                    info.TokensCacheRead = cache.GetInt64Property("read");
                    info.TokensCacheWrite = cache.GetInt64Property("write");
                }
            }
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

    public async Task<JsonElement> GetPendingQuestionsAsync(CancellationToken ct = default)
    {
        using var response = await Http.GetAsync("/question", ct);
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

    public static long GetInt64Property(this JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop)) return 0;
        if (prop.ValueKind == JsonValueKind.Number) return prop.GetInt64();
        return 0;
    }
}
