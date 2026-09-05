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
[JsonSerializable(typeof(Integration.SessionInfo))]
[JsonSerializable(typeof(RecentConnectionsStore.FileModel))]
[JsonSerializable(typeof(List<RecentConnection>))]
[JsonSerializable(typeof(SettingsStore.SettingsFileModel))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(List<Integration.QuestionInfo>))]
internal sealed partial class AppJsonContext : JsonSerializerContext
{
}
