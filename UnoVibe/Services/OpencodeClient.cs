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
    /// Get /path — returns the server instance path info including the working directory.
    /// Returns null on failure (older servers without this endpoint).
    /// </summary>
    public async Task<string?> GetDirectoryAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await Http.GetAsync("/path", ct);
            if (!response.IsSuccessStatusCode) return null;
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.TryGetProperty("directory", out var dir) ? dir.GetString() : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Get /vcs — VCS info for the workspace directory (or the server's default instance when
    /// null). Returns the current git branch, or null when the request fails / the route is
    /// missing. The <c>default_branch</c> field is not exposed to callers.
    /// </summary>
    public async Task<string?> GetBranchAsync(string? directory = null, CancellationToken ct = default)
    {
        try
        {
            using var response = await Http.GetAsync(DirectoryUrl("/vcs", directory), ct);
            if (!response.IsSuccessStatusCode) return null;
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var branch = doc.RootElement.GetStringProperty("branch");
            return branch.Length > 0 ? branch : null;
        }
        catch { return null; }
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
        var body = new CreateSessionRequest();
        if (!string.IsNullOrEmpty(title)) body.Title = title;
        if (!string.IsNullOrEmpty(agent)) body.Agent = agent;
        if (!string.IsNullOrEmpty(providerId) && !string.IsNullOrEmpty(modelId))
        {
            var model = new CreateSessionModelRequest
            {
                Id = modelId,
                ProviderID = providerId,
            };
            if (!string.IsNullOrEmpty(variant) && variant != "Default") model.Variant = variant;
            body.Model = model;
        }
        using var response = await Http.PostAsJsonAsync(url, body, AppJsonContext.Default.CreateSessionRequest, ct);
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
    }

    public async Task SendPromptAsync(string sessionId, string text,
        IReadOnlyList<ImageAttachment>? images = null,
        string? agent = null, string? providerId = null, string? modelId = null, string? variant = null,
        CancellationToken ct = default)
    {
        // Mirrors the opencode TUI/web clients: text + base64 data-URL file parts. The
        // text part is omitted when empty so an image-only prompt doesn't carry a blank one.
        var parts = new List<PromptPart>();
        if (!string.IsNullOrWhiteSpace(text)) parts.Add(new PromptPart { Type = "text", Text = text });
        if (images is not null)
            foreach (var image in images)
                parts.Add(new PromptPart { Type = "file", Mime = image.Mime, Filename = image.FileName, Url = image.DataUrl });
        var body = new SendPromptRequest { Parts = parts };
        if (!string.IsNullOrEmpty(agent)) body.Agent = agent;
        if (!string.IsNullOrEmpty(providerId) && !string.IsNullOrEmpty(modelId))
            body.Model = new SendPromptModelRequest { ProviderID = providerId, ModelID = modelId };
        if (!string.IsNullOrEmpty(variant) && variant != "Default") body.Variant = variant;
        using var response = await Http.PostAsJsonAsync($"/session/{sessionId}/prompt_async", body, AppJsonContext.Default.SendPromptRequest, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Patch /session/{id} — writes a new title for the session. This is how the TUI renames a
    /// session (and is also what the server's background title generator updates under the hood).
    /// </summary>
    public async Task UpdateSessionTitleAsync(string sessionId, string title, CancellationToken ct = default)
    {
        using var response = await Http.PatchAsJsonAsync($"/session/{sessionId}", new UpdateSessionTitleRequest { Title = title }, AppJsonContext.Default.UpdateSessionTitleRequest, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Post /session/{id}/abort — interrupts the currently-running turn and stops all
    /// ongoing AI processing / command execution for the session. Safe to call anytime.
    /// </summary>
    public async Task AbortAsync(string sessionId, CancellationToken ct = default)
    {
        using var response = await Http.PostAsJsonAsync($"/session/{sessionId}/abort", new EmptyRequest(), AppJsonContext.Default.EmptyRequest, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Post /session/{id}/revert — rewinds the session to just before the given user
    /// message (undo of the agent's reply). The server 409s when the session is busy, so
    /// callers should abort first (mirrors the TUI). Returns the updated session info
    /// JSON (its <c>revert</c> field carries <c>{ messageID, partID?, snapshot?, diff? }</c>).
    /// </summary>
    public async Task<JsonElement> RevertAsync(string sessionId, string messageId, CancellationToken ct = default)
    {
        using var response = await Http.PostAsJsonAsync($"/session/{sessionId}/revert", new RevertRequest { MessageID = messageId }, AppJsonContext.Default.RevertRequest, ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Post /session/{id}/unrevert — restores a reverted session (undo of undo). The
    /// server 400s when no revert is active.
    /// </summary>
    public async Task UnrevertAsync(string sessionId, CancellationToken ct = default)
    {
        using var response = await Http.PostAsJsonAsync($"/session/{sessionId}/unrevert", new EmptyRequest(), AppJsonContext.Default.EmptyRequest, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Post /session/{id}/fork — creates a new session by forking an existing one at a
    /// specific message point. Body is either empty (full-session fork) or <c>{ messageID }</c>;
    /// the server copies all messages strictly before the fork point (the forked-at message
    /// itself is excluded) and titles the new session "&lt;original&gt; (fork #N)". Returns the
    /// new session's info. Mirrors the TUI's <c>session.fork</c>.
    /// </summary>
    public async Task<SessionInfo?> ForkSessionAsync(string sessionId, string? messageId = null,
        CancellationToken ct = default)
    {
        var body = new ForkSessionRequest { MessageID = messageId };
        using var response = await Http.PostAsJsonAsync($"/session/{sessionId}/fork", body, AppJsonContext.Default.ForkSessionRequest, ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseSessionInfo(doc.RootElement);
    }

    /// <summary>
    /// Post /question/{requestID}/reply — answers a pending question request in the given
    /// workspace directory (or the server's default instance when null). Pending questions are
    /// per-instance, so the directory must match the one that owns the request or the reply
    /// will 404.
    /// </summary>
    public async Task ReplyQuestionAsync(string requestId, IReadOnlyList<IReadOnlyList<string>> answers,
        string? directory = null, CancellationToken ct = default)
    {
        var body = new ReplyQuestionRequest { Answers = answers };
        using var response = await Http.PostAsJsonAsync(
            DirectoryUrl($"/question/{requestId}/reply", directory),
            body, AppJsonContext.Default.ReplyQuestionRequest, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Post /question/{requestID}/reject — dismisses a pending question request (see <see cref="ReplyQuestionAsync"/> for the directory contract).</summary>
    public async Task RejectQuestionAsync(string requestId, string? directory = null, CancellationToken ct = default)
    {
        using var response = await Http.PostAsJsonAsync(
            DirectoryUrl($"/question/{requestId}/reject", directory),
            new EmptyRequest(), AppJsonContext.Default.EmptyRequest, ct);
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

    // ── Provider connect (mirrors the TUI's /connect dialog) ────────────────────

    /// <summary>
    /// Get /provider — the provider catalog <c>{ all, default, connected }</c> used by the
    /// model picker. <c>all</c> merges the Models.dev catalog with runtime providers;
    /// <c>connected</c> lists provider ids that have a stored credential. Returns null on
    /// failure.
    /// </summary>
    public async Task<ProviderListResult?> GetProvidersAsync(CancellationToken ct = default)
    {
        using var response = await Http.GetAsync("/provider", ct);
        if (!response.IsSuccessStatusCode) return null;
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.ProviderListResult, ct);
    }

    /// <summary>
    /// Get /provider/auth — the auth methods per provider
    /// (<c>Record&lt;providerID, ProviderAuthMethod[]&gt;</c>). Empty map on failure.
    /// </summary>
    public async Task<Dictionary<string, ProviderAuthMethod[]>> GetProviderAuthMethodsAsync(
        CancellationToken ct = default)
    {
        using var response = await Http.GetAsync("/provider/auth", ct);
        if (!response.IsSuccessStatusCode) return new Dictionary<string, ProviderAuthMethod[]>();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.DictionaryStringProviderAuthMethodArray, ct)
            ?? new Dictionary<string, ProviderAuthMethod[]>();
    }

    /// <summary>
    /// Put /auth/{providerID} — stores an API-key credential (the "api" auth method path;
    /// OAuth goes through <see cref="AuthorizeOAuthAsync"/>/<see cref="CompleteOAuthAsync"/> and
    /// writes the token server-side). <paramref name="metadata"/> carries the prompt inputs
    /// (e.g. account id, resource name).
    /// </summary>
    public async Task SetAuthAsync(string providerId, string key,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        var body = new AuthSetRequest { Key = key };
        if (metadata is { Count: > 0 }) body.Metadata = new Dictionary<string, string>(metadata);
        using var response = await Http.PutAsJsonAsync(
            $"/auth/{Uri.EscapeDataString(providerId)}", body, AppJsonContext.Default.AuthSetRequest, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Post /provider/{providerID}/oauth/authorize — starts an OAuth flow for the given auth
    /// method index, returning the URL to visit (plus whether a code is needed). Returns null
    /// when the server rejects the request.
    /// </summary>
    public async Task<OAuthAuthorization?> AuthorizeOAuthAsync(string providerId, int method,
        IReadOnlyDictionary<string, string>? inputs = null, CancellationToken ct = default)
    {
        var body = new OAuthAuthorizeRequest { Method = method };
        if (inputs is { Count: > 0 }) body.Inputs = new Dictionary<string, string>(inputs);
        using var response = await Http.PostAsJsonAsync(
            $"/provider/{Uri.EscapeDataString(providerId)}/oauth/authorize",
            body, AppJsonContext.Default.OAuthAuthorizeRequest, ct);
        if (!response.IsSuccessStatusCode) return null;
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.OAuthAuthorization, ct);
    }

    /// <summary>
    /// Post /provider/{providerID}/oauth/callback — completes an OAuth flow that was started
    /// with <see cref="AuthorizeOAuthAsync"/>. <paramref name="code"/> is required for "code"
    /// methods and omitted for "auto" methods. Writes the credential server-side.
    /// </summary>
    public async Task CompleteOAuthAsync(string providerId, int method, string? code = null,
        CancellationToken ct = default)
    {
        var body = new OAuthCallbackRequest { Method = method };
        if (!string.IsNullOrEmpty(code)) body.Code = code;
        using var response = await Http.PostAsJsonAsync(
            $"/provider/{Uri.EscapeDataString(providerId)}/oauth/callback",
            body, AppJsonContext.Default.OAuthCallbackRequest, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Get /session — lists sessions. Without a directory the list is scoped to the server's
    /// default project (instance); passing <paramref name="directory"/> (query param) lists the
    /// sessions for that specific workspace directory instead, which the default list excludes.
    /// </summary>
    public async Task<List<SessionInfo>> ListSessionsAsync(CancellationToken ct = default, string? directory = null)
    {
        var url = string.IsNullOrEmpty(directory) ? "/session" : $"/session?directory={Uri.EscapeDataString(directory)}";
        using var response = await Http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var list = new List<SessionInfo>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var info = ParseSessionInfo(item);
            if (info.Id.Length > 0) list.Add(info);
        }
        return list;
    }

    /// <summary>
    /// Get /session/{id} — fetches a single session's info. Used to resolve a session that
    /// isn't in the sidebar list yet (e.g. a subagent session opened right after it spawned).
    /// </summary>
    public async Task<SessionInfo?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        using var response = await Http.GetAsync($"/session/{sessionId}", ct);
        if (!response.IsSuccessStatusCode) return null;
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseSessionInfo(doc.RootElement);
    }

    private static SessionInfo ParseSessionInfo(JsonElement item)
    {
        var info = new SessionInfo
        {
            Id = item.GetStringProperty("id"),
            Title = item.GetStringProperty("title"),
            Directory = item.GetStringProperty("directory"),
            ProjectId = item.GetStringProperty("projectID"),
            Path = item.GetStringProperty("path"),
            Agent = item.GetStringProperty("agent"),
            ParentId = item.GetStringProperty("parentID"),
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
        return info;
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

    /// <summary>
    /// Get /question?directory= — lists pending question requests for the workspace directory
    /// (or the server's default instance when null). Pending requests are per-instance, so the
    /// directory must match the one that owns the requests or the replies will 404.
    /// </summary>
    public async Task<JsonElement> GetPendingQuestionsAsync(string? directory = null, CancellationToken ct = default)
    {
        using var response = await Http.GetAsync(DirectoryUrl("/question", directory), ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Get /permission?directory= — lists pending permission requests for the workspace directory
    /// (or the server's default instance when null). Pending requests are per-instance, so the
    /// directory must match the one that owns the request or the reply will 404. Returns an empty
    /// list for a non-array body.
    /// </summary>
    public async Task<List<PermissionRequestItem>> GetPendingPermissionsAsync(string? directory = null,
        CancellationToken ct = default)
    {
        var list = new List<PermissionRequestItem>();
        using var response = await Http.GetAsync(DirectoryUrl("/permission", directory), ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
        foreach (var request in doc.RootElement.EnumerateArray())
            list.Add(PermissionRequestItem.FromJson(request));
        return list;
    }

    /// <summary>
    /// Post /permission/{requestID}/reply — answers a pending permission request in the given
    /// workspace directory (or the server's default instance when null). <c>reply</c> is "once",
    /// "always", or "reject"; an optional message may be sent with a rejection.
    /// </summary>
    public async Task ReplyPermissionAsync(string requestId, string reply, string? message = null,
        string? directory = null, CancellationToken ct = default)
    {
        var body = new ReplyPermissionRequest { Reply = reply };
        if (!string.IsNullOrEmpty(message)) body.Message = message;
        using var response = await Http.PostAsJsonAsync(
            DirectoryUrl($"/permission/{requestId}/reply", directory),
            body, AppJsonContext.Default.ReplyPermissionRequest, ct);
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
            DirectoryUrl($"/mcp/{Uri.EscapeDataString(name)}/connect", directory), new EmptyRequest(), AppJsonContext.Default.EmptyRequest, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Post /mcp/{name}/disconnect — disconnects an MCP server (its status becomes "disabled").</summary>
    public async Task McpDisconnectAsync(string name, string? directory = null, CancellationToken ct = default)
    {
        using var response = await Http.PostAsJsonAsync(
            DirectoryUrl($"/mcp/{Uri.EscapeDataString(name)}/disconnect", directory), new EmptyRequest(), AppJsonContext.Default.EmptyRequest, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Appends the ?directory= query used by instance-scoped routes when a directory is known.</summary>
    private static string DirectoryUrl(string path, string? directory) =>
        string.IsNullOrEmpty(directory)
            ? path
            : $"{path}?directory={Uri.EscapeDataString(directory)}";

    // ── Suggestion-box data (Phase 2) ──────────────────────────────────────────────
    // Commands/skills prefer the legacy instance routes (/command, /skill with ?directory=):
    // on the current dev server (1.17.x) those return the FULL list — user commands, MCP
    // entries, skills folded in, and the `source` field — while the newer /api/* HttpApi
    // surface returns only built-ins (init/review; customize-opencode). The /api/* wrapped
    // forms ({ location: {...}, data: [...] }) are kept as a fallback for servers that drop
    // the legacy routes. Files have no legacy route (it 404s), so /api/fs/find is primary.

    /// <summary>
    /// Lists every server/user/MCP/skill command for the directory: legacy <c>/command?directory=</c>
    /// first, falling back to <c>/api/command?location[directory]=</c>. Returns an empty list on
    /// failure so the suggestion box degrades gracefully.
    /// </summary>
    public async Task<List<ServerCommandItem>> GetCommandsAsync(string? directory = null,
        CancellationToken ct = default)
    {
        var list = new List<ServerCommandItem>();
        try
        {
            var items = await FetchItemArrayAsync(DirectoryUrl("/command", directory), ct)
                ?? await FetchItemArrayAsync(LocationUrl("/api/command", directory), ct);
            if (items is null) return list;
            foreach (var item in items)
            {
                var source = item.GetStringProperty("source");
                if (source.Length == 0) source = "command";
                list.Add(new ServerCommandItem
                {
                    Name = item.GetStringProperty("name"),
                    Description = item.GetStringProperty("description"),
                    Source = source,
                    Subtask = item.TryGetProperty("subtask", out var subtask)
                        && subtask.ValueKind == JsonValueKind.True,
                    Hints = GetStringArray(item, "hints"),
                });
            }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
        catch (System.Text.Json.JsonException) { }
        return list;
    }

    /// <summary>
    /// Lists skills available for the directory: legacy <c>/skill?directory=</c> first, falling
    /// back to <c>/api/skill?location[directory]=</c>. Empty list on failure.
    /// </summary>
    public async Task<List<ServerSkillItem>> GetSkillsAsync(string? directory = null,
        CancellationToken ct = default)
    {
        var list = new List<ServerSkillItem>();
        try
        {
            var items = await FetchItemArrayAsync(DirectoryUrl("/skill", directory), ct)
                ?? await FetchItemArrayAsync(LocationUrl("/api/skill", directory), ct);
            if (items is null) return list;
            foreach (var item in items)
            {
                list.Add(new ServerSkillItem
                {
                    Name = item.GetStringProperty("name"),
                    Description = item.GetStringProperty("description"),
                });
            }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
        catch (System.Text.Json.JsonException) { }
        return list;
    }

    /// <summary>
    /// Get /api/fs/find — fuzzy file search. The server pre-filters and pre-ranks results
    /// (frecency, fuzzy score, filename bonus), so callers must NOT re-sort. Empty on failure.
    /// (The legacy <c>/fs/find</c> route 404s on current servers, so this has no legacy fallback.)
    /// </summary>
    public async Task<List<FileSystemEntry>> FindFilesAsync(string query, string? directory = null,
        string? type = null, int limit = 20, CancellationToken ct = default)
    {
        var list = new List<FileSystemEntry>();
        try
        {
            var url = LocationUrl("/api/fs/find", directory);
            var separator = url.Contains('?') ? '&' : '?';
            url += $"{separator}query={Uri.EscapeDataString(query)}";
            if (!string.IsNullOrEmpty(type)) url += $"&type={Uri.EscapeDataString(type)}";
            url += $"&limit={limit}";

            using var response = await Http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return list;
            foreach (var item in await GetApiDataAsync(response, ct))
            {
                list.Add(new FileSystemEntry
                {
                    Path = item.GetStringProperty("path"),
                    Type = item.GetStringProperty("type"),
                });
            }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
        catch (System.Text.Json.JsonException) { }
        return list;
    }

    /// <summary>
    /// Fetches a URL and returns the item array, or null when the response isn't a usable
    /// JSON list. Accepts both a bare array (legacy instance routes) and a wrapped
    /// <c>{ location, data }</c> envelope (/api/* surface). The location field is ignored
    /// (ChatStore does not track a workspace id — only the directory is ever passed).
    /// </summary>
    private async Task<IReadOnlyList<JsonElement>?> FetchItemArrayAsync(string url,
        CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return null;
        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            var items = new List<JsonElement>();
            foreach (var item in root.EnumerateArray()) items.Add(item.Clone());
            return items;
        }
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            var items = new List<JsonElement>();
            foreach (var item in data.EnumerateArray()) items.Add(item.Clone());
            return items;
        }
        return null;
    }

    /// <summary>
    /// Builds the deep-object location query for the /api/* endpoints. The location param
    /// serializes as <c>location[directory]=...</c> (OpenAPI deepObject explode); the brackets are
    /// percent-encoded (<c>%5B</c>/<c>%5D</c>) so the URI stays valid — the server accepts both
    /// forms. Only the directory is set — UnoVibe uses plain directories, not workspace-v2 ids.
    /// </summary>
    private static string LocationUrl(string path, string? directory) =>
        string.IsNullOrEmpty(directory)
            ? path
            : $"{path}?location%5Bdirectory%5D={Uri.EscapeDataString(directory)}";

    /// <summary>Enumerates the <c>data</c> array of an /api/* wrapper response.</summary>
    private static async Task<IReadOnlyList<JsonElement>> GetApiDataAsync(HttpResponseMessage response,
        CancellationToken ct)
    {
        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array) return Array.Empty<JsonElement>();
        var items = new List<JsonElement>();
        foreach (var item in data.EnumerateArray()) items.Add(item.Clone());
        return items;
    }

    private static string[] GetStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<string>();
        foreach (var item in prop.EnumerateArray()) list.Add(item.GetString() ?? "");
        return list.ToArray();
    }
}

internal static class OpencodeClientExtensions
{
    public static string GetStringProperty(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var prop) ? prop.GetString() ?? "" : "";

    public static long GetInt64Property(this JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop)) return 0;
        if (prop.ValueKind == JsonValueKind.Number) return prop.GetInt64();
        return 0;
    }

    public static bool GetBoolProperty(this JsonElement element, string name, bool fallback = false)
    {
        if (!element.TryGetProperty(name, out var prop)) return fallback;
        if (prop.ValueKind == JsonValueKind.True) return true;
        if (prop.ValueKind == JsonValueKind.False) return false;
        if (prop.ValueKind == JsonValueKind.String && prop.GetString() == "true") return true;
        return fallback;
    }
}
