using System.Threading.Channels;

namespace UnoVibe.Integration;

/// <summary>
/// An event received on the SSE stream. The payload <c>Properties</c> is kept as
/// JSON so the store can parse each event type as needed (the OpenAPI spec does not
/// describe the SSE payloads, so these are hand-defined from the server schema).
/// </summary>
public sealed record OpencodeEvent(string? Id, string Type, JsonElement Properties);


partial class OpencodeClient
{
    /// <summary>
    /// Reads the server sent events stream and writes parsed events to a channel.
    /// </summary>
    /// <remarks>
    /// This API is blocking. Recommended to run in a new task.
    /// </remarks>
    public async Task ReadEventAsync(ChannelWriter<OpencodeEvent> writer, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/event");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var payload = line["data:".Length..].TrimStart();
            if (payload.Length == 0) continue;

            try
            {
                var evt = JsonSerializer.Deserialize(payload, AppJsonContext.Default.OpencodeEvent);
                if (evt is not null) await writer.WriteAsync(evt, ct);
            }
            catch (JsonException)
            {
                // Skip malformed events.
            }
        }
    }
}
