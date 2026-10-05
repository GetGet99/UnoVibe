using System.Text.Json;
using System.Text.Json.Serialization;
using UnoVibe.Integration.Events;

namespace UnoVibe.Helpers;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = false,
    UseStringEnumConverter = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(Integration.SessionInfo))]
[JsonSerializable(typeof(RecentConnectionsStore.FileModel))]
[JsonSerializable(typeof(List<RecentConnection>))]
[JsonSerializable(typeof(SettingsStore.SettingsFileModel))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(List<Integration.QuestionInfo>))]
[JsonSerializable(typeof(List<TodoInfo>))]
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
[JsonSerializable(typeof(ToolMetadata))]
internal sealed partial class AppJsonContext : JsonSerializerContext
{
}
