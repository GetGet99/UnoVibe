using System.Text.Json;
using System.Threading.Channels;

namespace UnoVibe.Services;

/// <summary>
/// An event received on the SSE stream. The payload <c>Properties</c> is kept as
/// JSON so the store can parse each event type as needed (the OpenAPI spec does not
/// describe the SSE payloads, so these are hand-defined from the server schema).
/// </summary>
public sealed record OpencodeEvent(string? Id, string Type, JsonElement Properties);

/// <summary>
/// Reads the <c>/event</c> server-sent-events stream and writes parsed events to a channel.
/// </summary>
public static class EventStreamReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task ReadAsync(HttpClient http, string eventUrl, ChannelWriter<OpencodeEvent> writer, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, eventUrl);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
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
                var evt = JsonSerializer.Deserialize<OpencodeEvent>(payload, Json);
                if (evt is not null) await writer.WriteAsync(evt, ct);
            }
            catch (JsonException)
            {
                // Skip malformed events.
            }
        }
    }
}
