using System.Text.Json;
using System.Text.Json.Serialization;
using UnoVibe.Integration.Events;
using UnoVibe.Models;

namespace UnoVibe.Services;

/// <summary>
/// Source-generated System.Text.Json context covering every type the app (de)serializes.
/// Required for native AOT: reflection-based JSON (de)serialization is unavailable when
/// trimming/AOT compiling, so all <c>JsonSerializer</c> and <c>PostAsJsonAsync</c>/<c>PatchAsJsonAsync</c>
/// call sites must route through <see cref="Default"/>. Matches the previous reflection
/// options (<c>JsonSerializerDefaults.Web</c>, non-indented output).
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = false,
    UseStringEnumConverter = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip)]
[JsonSerializable(typeof(Integration.SessionInfo))]
[JsonSerializable(typeof(RecentConnectionsStore.FileModel))]
[JsonSerializable(typeof(List<RecentConnection>))]
[JsonSerializable(typeof(SettingsStore.SettingsFileModel))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(List<Integration.QuestionInfo>))]
// --- Event types used by EventSource ---
[JsonSerializable(typeof(MessageUpdatedEvent))]
[JsonSerializable(typeof(MessagePartUpdatedEvent))]
[JsonSerializable(typeof(MessagePartDeltaEvent))]
[JsonSerializable(typeof(MessagePartRemovedEvent))]
[JsonSerializable(typeof(MessageRemovedEvent))]
[JsonSerializable(typeof(SessionCrudEvent))]
[JsonSerializable(typeof(SessionStatusEvent))]
[JsonSerializable(typeof(SessionErrorEvent))]
[JsonSerializable(typeof(SessionDiffEvent))]
[JsonSerializable(typeof(SessionIdleEvent))]
[JsonSerializable(typeof(SessionCompactedEvent))]
[JsonSerializable(typeof(QuestionAskedEvent))]
[JsonSerializable(typeof(QuestionRepliedEvent))]
[JsonSerializable(typeof(QuestionRejectedEvent))]
[JsonSerializable(typeof(PermissionAskedEvent))]
[JsonSerializable(typeof(PermissionRepliedEvent))]
[JsonSerializable(typeof(FileEditedEvent))]
[JsonSerializable(typeof(FileWatcherUpdatedEvent))]
[JsonSerializable(typeof(VcsBranchUpdatedEvent))]
[JsonSerializable(typeof(TodoUpdatedEvent))]
[JsonSerializable(typeof(LspUpdatedEvent))]
[JsonSerializable(typeof(CommandExecutedEvent))]
[JsonSerializable(typeof(McpToolsChangedEvent))]
[JsonSerializable(typeof(McpBrowserOpenFailedEvent))]
[JsonSerializable(typeof(ServerConnectedEvent))]
[JsonSerializable(typeof(ServerInstanceDisposedEvent))]
[JsonSerializable(typeof(TuiToastShowEvent))]
internal sealed partial class AppJsonContext : JsonSerializerContext
{
}
