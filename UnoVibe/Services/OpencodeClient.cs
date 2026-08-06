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

    /// <summary>
    /// Post /session — creates a session. When <paramref name="title"/> is null/empty the server
    /// assigns a timestamped default title ("New session - &lt;ISO&gt;"), which its title agent
    /// later replaces with a generated name on the first prompt. Passing an explicit title skips
    /// that auto-generation.
    /// </summary>
    public async Task<string?> CreateSessionAsync(string? title = null, string? directory = null,
        string? agent = null, string? providerId = null, string? modelId = null, string? variant = null,
        CancellationToken ct = default)
    {
        var url = string.IsNullOrEmpty(directory) ? "/session" : $"/session?directory={Uri.EscapeDataString(directory)}";
        var body = new Dictionary<string, object?>();
        if (!string.IsNullOrEmpty(title)) body["title"] = title;
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

    /// <summary>
    /// Patch /session/{id} — writes a new title for the session. This is how the TUI renames a
    /// session (and is also what the server's background title generator updates under the hood).
    /// </summary>
    public async Task UpdateSessionTitleAsync(string sessionId, string title, CancellationToken ct = default)
    {
        using var response = await Http.PatchAsJsonAsync($"/session/{sessionId}", new { title }, Json, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Post /session/{id}/abort — interrupts the currently-running turn and stops all
    /// ongoing AI processing / command execution for the session. Safe to call anytime.
    /// </summary>
    public async Task AbortAsync(string sessionId, CancellationToken ct = default)
    {
        using var response = await Http.PostAsJsonAsync($"/session/{sessionId}/abort", new { }, Json, ct);
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

    /// <summary>
    /// Get /session/status — snapshot map of currently-busy sessions
    /// (<c>{ sessionID: { type: "busy"|"retry", ... } }</c>). Idle sessions are absent,
    /// so an entry whose <c>type</c> is missing/empty is treated as idle by callers.
    /// </summary>
    public async Task<Dictionary<string, string>> GetSessionStatusAsync(CancellationToken ct = default)
    {
        var map = new Dictionary<string, string>();
        using var response = await Http.GetAsync("/session/status", ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return map;
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var type = prop.Value.GetStringProperty("type");
            if (type.Length > 0) map[prop.Name] = type;
        }
        return map;
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

    public async Task<JsonElement> GetPendingPermissionsAsync(CancellationToken ct = default)
    {
        using var response = await Http.GetAsync("/permission", ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Post /permission/{requestID}/reply — answers a pending permission request.
    /// <c>reply</c> is "once", "always", or "reject"; an optional message may be sent
    /// with a rejection.
    /// </summary>
    public async Task ReplyPermissionAsync(string requestId, string reply, string? message = null,
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["reply"] = reply,
        };
        if (!string.IsNullOrEmpty(message)) body["message"] = message;
        using var response = await Http.PostAsJsonAsync($"/permission/{requestId}/reply", body, Json, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Get /mcp — status of all MCP servers for the given workspace directory (or the
    /// server's default directory when null). Returns a map of server name → status, where
    /// status is one of "connected", "disabled", "failed" (with an error message),
    /// "needs_auth", or "needs_client_registration" (with an error message).
    /// </summary>
    public async Task<Dictionary<string, McpServerInfo>> GetMcpStatusAsync(string? directory = null,
        CancellationToken ct = default)
    {
        var url = DirectoryUrl("/mcp", directory);
        using var response = await Http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var map = new Dictionary<string, McpServerInfo>();
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return map;
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var status = prop.Value.GetStringProperty("status");
            var error = prop.Value.GetStringProperty("error");
            if (status.Length > 0) map[prop.Name] = new McpServerInfo { Status = status, Error = error };
        }
        return map;
    }

    /// <summary>Post /mcp/{name}/connect — (re)connects an MCP server.</summary>
    public async Task McpConnectAsync(string name, string? directory = null, CancellationToken ct = default)
    {
        using var response = await Http.PostAsJsonAsync(
            DirectoryUrl($"/mcp/{Uri.EscapeDataString(name)}/connect", directory), new { }, Json, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Post /mcp/{name}/disconnect — disconnects an MCP server (its status becomes "disabled").</summary>
    public async Task McpDisconnectAsync(string name, string? directory = null, CancellationToken ct = default)
    {
        using var response = await Http.PostAsJsonAsync(
            DirectoryUrl($"/mcp/{Uri.EscapeDataString(name)}/disconnect", directory), new { }, Json, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Appends the ?directory= query used by instance-scoped routes when a directory is known.</summary>
    private static string DirectoryUrl(string path, string? directory) =>
        string.IsNullOrEmpty(directory)
            ? path
            : $"{path}?directory={Uri.EscapeDataString(directory)}";
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
