namespace UnoVibe.Integration;

/// <summary>POST /session/{id}/prompt_async body.</summary>
public sealed class SendPromptRequest
{
    public required List<PromptPart> Parts { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Agent { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SendPromptModelRequest? Model { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Variant { get; set; }
}

/// <summary>
/// One prompt part. Text parts serialize <c>{type:"text", text}</c>; file (image) parts
/// serialize <c>{type:"file", mime, filename, url}</c> — the unused fields are ignored.
/// </summary>
public sealed class PromptPart
{
    public required string Type { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mime { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Filename { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }
}

/// <summary>The <c>model</c> field of <see cref="SendPromptRequest"/>.</summary>
public sealed class SendPromptModelRequest
{
    public string? ProviderID { get; set; }
    public string? ModelID { get; set; }
}

partial class OpencodeClient
{
    public Task SendPromptAsync(string sessionId, SendPromptRequest request,
        CancellationToken ct = default)
        => PostAsync(
            $"/session/{sessionId}/prompt_async",
            request,
            AppJsonContext.Default.SendPromptRequest,
            ct
        );
}
