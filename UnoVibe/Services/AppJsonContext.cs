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
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(CreateSessionRequest))]
[JsonSerializable(typeof(SendPromptRequest))]
[JsonSerializable(typeof(UpdateSessionTitleRequest))]
[JsonSerializable(typeof(EmptyRequest))]
[JsonSerializable(typeof(RevertRequest))]
[JsonSerializable(typeof(ForkSessionRequest))]
[JsonSerializable(typeof(ReplyQuestionRequest))]
[JsonSerializable(typeof(ReplyPermissionRequest))]
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
