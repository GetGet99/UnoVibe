namespace UnoVibe.Integration.Events;

#region Server / lifecycle

public sealed class ServerConnectedEvent { }

public sealed class ServerDisposedEvent { }

public sealed class ServerInstanceDisposedEvent
{
    public required string Directory { get; set; }
}

#endregion

#region File

public sealed class FileEditedEvent
{
    public required string File { get; set; }
}

public sealed class FileWatcherUpdatedEvent
{
    public required string File { get; set; }
    public FileWatcherEventType Event { get; set; }
}

#endregion

#region VCS

public sealed class VcsBranchUpdatedEvent
{
    public string? Branch { get; set; }
}

#endregion

#region Todo

public sealed class TodoUpdatedEvent
{
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    public List<TodoItem> Todos { get; set; } = [];
}

public sealed class TodoItem
{
    public required string Content { get; set; }
    public required string Status { get; set; }
    public required string Priority { get; set; }
}

#endregion

#region Command

public sealed class CommandExecutedEvent
{
    public required string Name { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    public required string Arguments { get; set; }
    [JsonPropertyName("messageID")] public required string MessageId { get; set; }
}

#endregion

#region MCP

public sealed class McpToolsChangedEvent
{
    public required string Server { get; set; }
}

public sealed class McpBrowserOpenFailedEvent
{
    [JsonPropertyName("mcpName")] public required string McpName { get; set; }
    public required string Url { get; set; }
}

#endregion

#region TUI

public sealed class TuiPromptAppendEvent
{
    public required string Text { get; set; }
}

public sealed class TuiCommandExecuteEvent
{
    public required string Command { get; set; }
}

public sealed class TuiToastShowEvent
{
    public string? Title { get; set; }
    public required string Message { get; set; }
    public string Variant { get; set; } = "info";
    public double Duration { get; set; } = 5000;
}

public sealed class TuiSessionSelectEvent
{
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
}

#endregion

#region Project

public sealed class ProjectUpdatedEvent
{
    public required string Id { get; set; }
    public required string Worktree { get; set; }
    public string? Vcs { get; set; }
    public string? Name { get; set; }
    public ProjectIcon? Icon { get; set; }
    public ProjectCommands? Commands { get; set; }
    public required ProjectTimeInfo Time { get; set; }
    public List<string> Sandboxes { get; set; } = [];
}

public sealed class ProjectIcon
{
    public string? Url { get; set; }
    public string? Override { get; set; }
    public string? Color { get; set; }
}

public sealed class ProjectCommands
{
    public string? Start { get; set; }
}

public sealed class ProjectTimeInfo
{
    public double Created { get; set; }
    public double Updated { get; set; }
    public double? Initialized { get; set; }
}

public sealed class ProjectDirectoriesUpdatedEvent
{
    [JsonPropertyName("projectID")] public required string ProjectId { get; set; }
}

#endregion

#region PTY

public sealed class PtyCreatedEvent
{
    public required PtyInfo Info { get; set; }
}

public sealed class PtyUpdatedEvent
{
    public required PtyInfo Info { get; set; }
}

public sealed class PtyExitedEvent
{
    public required string Id { get; set; }
    public double ExitCode { get; set; }
}

public sealed class PtyDeletedEvent
{
    public required string Id { get; set; }
}

public sealed class PtyInfo
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Command { get; set; }
    public List<string> Args { get; set; } = [];
    public required string Cwd { get; set; }
    public PtyStatus Status { get; set; }
    public double Pid { get; set; }
    public double? ExitCode { get; set; }
}

#endregion

#region Workspace

public sealed class WorkspaceReadyEvent
{
    public required string Name { get; set; }
}

public sealed class WorkspaceFailedEvent
{
    public required string Message { get; set; }
}

public sealed class WorkspaceStatusEvent
{
    [JsonPropertyName("workspaceID")] public required string WorkspaceId { get; set; }
    public WorkspaceConnectionStatus Status { get; set; }
}

#endregion

#region Worktree

public sealed class WorktreeReadyEvent
{
    public required string Name { get; set; }
    public string? Branch { get; set; }
}

public sealed class WorktreeFailedEvent
{
    public required string Message { get; set; }
}

#endregion

#region Installation

public sealed class InstallationUpdatedEvent
{
    public required string Version { get; set; }
}

public sealed class InstallationUpdateAvailableEvent
{
    public required string Version { get; set; }
}

#endregion

#region Plugin / Reference / Catalog / LSP / Integration / ModelsDev

public sealed class PluginAddedEvent
{
    public required string Id { get; set; }
}

public sealed class ReferenceUpdatedEvent { }

public sealed class CatalogUpdatedEvent { }

public sealed class ModelsDevRefreshedEvent { }

public sealed class LspUpdatedEvent { }

public sealed class IntegrationUpdatedEvent { }

public sealed class IntegrationConnectionUpdatedEvent
{
    [JsonPropertyName("integrationID")] public required string IntegrationId { get; set; }
}

#endregion
