using System.Text.Json;
using System.Text.Json.Serialization;
using UnoVibe.Models;

namespace UnoVibe.Services;

/// <summary>
/// Source-generated System.Text.Json context covering every type the app (de)serializes.
/// Required for native AOT: reflection-based JSON (de)serialization is unavailable when
/// trimming/AOT compiling, so all <c>JsonSerializer</c> and <c>PostAsJsonAsync</c>/<c>PatchAsJsonAsync</c>
/// call sites must route through <see cref="Default"/>. Matches the previous reflection
/// options (<c>JsonSerializerDefaults.Web</c>, non-indented output).
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = false)]
[JsonSerializable(typeof(OpencodeEvent))]
[JsonSerializable(typeof(RecentConnectionsStore.FileModel))]
[JsonSerializable(typeof(List<RecentConnection>))]
[JsonSerializable(typeof(SettingsStore.SettingsFileModel))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(CreateSessionRequest))]
[JsonSerializable(typeof(SendPromptRequest))]
[JsonSerializable(typeof(UpdateSessionTitleRequest))]
[JsonSerializable(typeof(EmptyRequest))]
[JsonSerializable(typeof(RevertRequest))]
[JsonSerializable(typeof(ForkSessionRequest))]
[JsonSerializable(typeof(ReplyQuestionRequest))]
[JsonSerializable(typeof(ReplyPermissionRequest))]
[JsonSerializable(typeof(ProviderListResult))]
[JsonSerializable(typeof(ProviderAuthMethod))]
[JsonSerializable(typeof(Dictionary<string, ProviderAuthMethod[]>))]
[JsonSerializable(typeof(AuthSetRequest))]
[JsonSerializable(typeof(OAuthAuthorization))]
[JsonSerializable(typeof(OAuthAuthorizeRequest))]
[JsonSerializable(typeof(OAuthCallbackRequest))]
internal sealed partial class AppJsonContext : JsonSerializerContext
{
}

// ── OpencodeClient request bodies ─────────────────────────────────────────────
// Named types replacing the previous anonymous/Dictionary bodies so they can be
// source-generated. Nullable properties carry [JsonIgnore(WhenWritingNull)] to keep
// the exact wire shape of the old conditional dictionary construction.

/// <summary>POST /session body.</summary>
internal sealed class CreateSessionRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Agent { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CreateSessionModelRequest? Model { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Variant { get; set; }
}

/// <summary>The <c>model</c> field of <see cref="CreateSessionRequest"/>.</summary>
internal sealed class CreateSessionModelRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProviderID { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Variant { get; set; }
}

/// <summary>POST /session/{id}/prompt_async body.</summary>
internal sealed class SendPromptRequest
{
    public List<PromptPart> Parts { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Agent { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SendPromptModelRequest? Model { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Variant { get; set; }
}

/// <summary>The <c>model</c> field of <see cref="SendPromptRequest"/>.</summary>
internal sealed class SendPromptModelRequest
{
    public string? ProviderID { get; set; }

    public string? ModelID { get; set; }
}

/// <summary>
/// One prompt part. Text parts serialize <c>{type:"text", text}</c>; file (image) parts
/// serialize <c>{type:"file", mime, filename, url}</c> — the unused fields are ignored.
/// </summary>
internal sealed class PromptPart
{
    public string Type { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mime { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Filename { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }
}

/// <summary>PATCH /session/{id} body (title rename).</summary>
internal sealed class UpdateSessionTitleRequest
{
    public string Title { get; set; } = "";
}

/// <summary>Empty JSON object body for endpoints that take <c>{}</c>.</summary>
internal sealed class EmptyRequest
{
}

/// <summary>POST /session/{id}/revert body.</summary>
internal sealed class RevertRequest
{
    public string MessageID { get; set; } = "";
}

/// <summary>POST /session/{id}/fork body: empty for a full-session fork, <c>{messageID}</c> otherwise.</summary>
internal sealed class ForkSessionRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MessageID { get; set; }
}

/// <summary>POST /question/{requestId}/reply body.</summary>
internal sealed class ReplyQuestionRequest
{
    public IReadOnlyList<IReadOnlyList<string>> Answers { get; set; } = Array.Empty<IReadOnlyList<string>>();
}

/// <summary>POST /permission/{requestID}/reply body.</summary>
internal sealed class ReplyPermissionRequest
{
    public string Reply { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }
}

// ── Provider connect (mirrors the TUI's /connect dialog API surface) ──────────

/// <summary>
/// GET /provider response — <c>{ all, default, connected }</c>. <c>all</c> is the provider
/// catalog (Models.dev merged with runtime providers), <c>connected</c> the provider ids with
/// a stored credential.
/// </summary>
public sealed class ProviderListResult
{
    public List<ProviderInfo>? All { get; set; }

    public Dictionary<string, string>? Default { get; set; }

    public List<string>? Connected { get; set; }
}

/// <summary>One provider entry from <see cref="ProviderListResult.All"/>.</summary>
public sealed class ProviderInfo
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";
}

/// <summary>
/// One auth method for a provider (GET /provider/auth): either an API-key entry
/// (<c>type == "api"</c>) or an OAuth flow (<c>type == "oauth"</c>). Optional prompts
/// (text/select inputs) must be answered before completing the method.
/// </summary>
public sealed class ProviderAuthMethod
{
    public string Type { get; set; } = "";

    public string Label { get; set; } = "";

    public List<AuthPrompt>? Prompts { get; set; }
}

/// <summary>A text or select input the auth method wants answered (e.g. a base URL, account id, deployment type).</summary>
public sealed class AuthPrompt
{
    public string Type { get; set; } = "";

    public string Key { get; set; } = "";

    public string Message { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Placeholder { get; set; }

    public List<AuthPromptOption>? Options { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AuthWhen? When { get; set; }
}

/// <summary>An option of a select <see cref="AuthPrompt"/>.</summary>
public sealed class AuthPromptOption
{
    public string Label { get; set; } = "";

    public string Value { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hint { get; set; }
}

/// <summary>Condition gating an <see cref="AuthPrompt"/> on an earlier prompt's value.</summary>
public sealed class AuthWhen
{
    public string Key { get; set; } = "";

    public string Op { get; set; } = "";

    public string Value { get; set; } = "";
}

/// <summary>
/// PUT /auth/{providerID} body — stores an API-key credential (type "api"). The TUI's
/// <c>/connect</c> flow uses exactly this shape; OAuth tokens are written server-side by the
/// oauth callback endpoint instead.
/// </summary>
internal sealed class AuthSetRequest
{
    public string Type { get; set; } = "api";

    public string Key { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>POST /provider/{providerID}/oauth/authorize response.</summary>
public sealed class OAuthAuthorization
{
    public string Url { get; set; } = "";

    /// <summary>"auto" (no code — the callback completes as-is) or "code" (paste the authorization code).</summary>
    public string Method { get; set; } = "";

    public string Instructions { get; set; } = "";
}

/// <summary>POST /provider/{providerID}/oauth/authorize body.</summary>
internal sealed class OAuthAuthorizeRequest
{
    public int Method { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Inputs { get; set; }
}

/// <summary>POST /provider/{providerID}/oauth/callback body (code optional for "auto" methods).</summary>
internal sealed class OAuthCallbackRequest
{
    public int Method { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; set; }
}
