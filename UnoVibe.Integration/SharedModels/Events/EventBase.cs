namespace UnoVibe.Integration.Events;

#region Enums

public enum SessionStatusType { Idle, Busy, Retry }
public enum ToolStateStatus { Pending, Running, Completed, Error }
public enum PermissionReply { Once, Always, Reject }
public enum FileWatcherEventType { Add, Change, Unlink }
public enum CompactionReason { Auto, Manual }
public enum WorkspaceConnectionStatus { Connected, Connecting, Disconnected, Error }
public enum PtyStatus { Running, Exited }
public enum TodoStatus { Pending, InProgress, Completed, Cancelled }
public enum TodoPriority { High, Medium, Low }
public enum FileDiffStatus { Added, Deleted, Modified }

#endregion

#region Discriminated union base classes

/// <summary>Base class for all opencode SSE event payloads.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "role")]
[JsonDerivedType(typeof(UserMessageInfo), "user")]
[JsonDerivedType(typeof(AssistantMessageInfo), "assistant")]
public abstract class MessageInfo { }

/// <summary>Base class for all message part types, discriminated on <c>type</c>.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextPart), "text")]
[JsonDerivedType(typeof(ReasoningPart), "reasoning")]
[JsonDerivedType(typeof(FilePart), "file")]
[JsonDerivedType(typeof(ToolPart), "tool")]
[JsonDerivedType(typeof(StepStartPart), "step-start")]
[JsonDerivedType(typeof(StepFinishPart), "step-finish")]
[JsonDerivedType(typeof(SnapshotPart), "snapshot")]
[JsonDerivedType(typeof(PatchPart), "patch")]
[JsonDerivedType(typeof(AgentPart), "agent")]
[JsonDerivedType(typeof(RetryPart), "retry")]
[JsonDerivedType(typeof(CompactionPart), "compaction")]
[JsonDerivedType(typeof(SubtaskPart), "subtask")]
public abstract class Part
{
    public required string Id { get; set; }
    [JsonPropertyName("sessionID")] public required string SessionId { get; set; }
    [JsonPropertyName("messageID")] public required string MessageId { get; set; }
}

/// <summary>Base class for tool state, discriminated on <c>status</c>.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "status")]
[JsonDerivedType(typeof(ToolStatePending), "pending")]
[JsonDerivedType(typeof(ToolStateRunning), "running")]
[JsonDerivedType(typeof(ToolStateCompleted), "completed")]
[JsonDerivedType(typeof(ToolStateError), "error")]
public abstract class ToolState
{
    public ToolStateStatus Status { get; set; }
    public JsonElement Input { get; set; }
}

/// <summary>Base class for file part source, discriminated on <c>type</c>.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(FileSource), "file")]
[JsonDerivedType(typeof(SymbolSource), "symbol")]
[JsonDerivedType(typeof(ResourceSource), "resource")]
public abstract class FilePartSource { }

/// <summary>Base class for assistant errors, discriminated on <c>name</c>.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "name")]
[JsonDerivedType(typeof(ProviderAuthError), "ProviderAuthError")]
[JsonDerivedType(typeof(UnknownAssistantError), "UnknownError")]
[JsonDerivedType(typeof(OutputLengthError), "MessageOutputLengthError")]
[JsonDerivedType(typeof(AbortedError), "MessageAbortedError")]
[JsonDerivedType(typeof(StructuredOutputError), "StructuredOutputError")]
[JsonDerivedType(typeof(ContextOverflowError), "ContextOverflowError")]
[JsonDerivedType(typeof(ContentFilterError), "ContentFilterError")]
[JsonDerivedType(typeof(ApiAssistantError), "APIError")]
public abstract class AssistantError { }

/// <summary>Base class for session status payload, discriminated on <c>type</c>.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SessionStatusIdle), "idle")]
[JsonDerivedType(typeof(SessionStatusBusy), "busy")]
[JsonDerivedType(typeof(SessionStatusRetry), "retry")]
public abstract class SessionStatusPayload { }

#endregion

#region Shared sub-models

public sealed class SnapshotFileDiff
{
    public string? File { get; set; }
    public string? Patch { get; set; }
    public double Additions { get; set; }
    public double Deletions { get; set; }
    public FileDiffStatus? Status { get; set; }
}

public sealed class TokenUsage
{
    public double Input { get; set; }
    public double Output { get; set; }
    public double Reasoning { get; set; }
    public TokenCache Cache { get; set; } = new();
}

public sealed class TokenCache
{
    public double Read { get; set; }
    public double Write { get; set; }
}

public sealed class TimeRange
{
    public double Start { get; set; }
    public double? End { get; set; }
}

public sealed class ToolTimeRange
{
    public double Start { get; set; }
    public double? End { get; set; }
    public double? Compacted { get; set; }
}

public sealed class AssistantPath
{
    public required string Cwd { get; set; }
    public required string Root { get; set; }
}

public sealed class SourcePosition
{
    public double Line { get; set; }
    public double Character { get; set; }
}

public sealed class SourceRange
{
    public SourcePosition Start { get; set; } = new();
    public SourcePosition End { get; set; } = new();
}

public sealed class FileSourceText
{
    public required string Value { get; set; }
    public double Start { get; set; }
    public double End { get; set; }
}

public sealed class AgentPartSource
{
    public required string Value { get; set; }
    public double Start { get; set; }
    public double End { get; set; }
}

#endregion

#region Assistant error types

public sealed class ProviderAuthError : AssistantError
{
    [JsonPropertyName("providerID")] public required string ProviderId { get; set; }
    public required string Message { get; set; }
}

public sealed class UnknownAssistantError : AssistantError
{
    public required string Message { get; set; }
    public string? Ref { get; set; }
}

public sealed class OutputLengthError : AssistantError { }

public sealed class AbortedError : AssistantError
{
    public required string Message { get; set; }
}

public sealed class StructuredOutputError : AssistantError
{
    public required string Message { get; set; }
    public double Retries { get; set; }
}

public sealed class ContextOverflowError : AssistantError
{
    public required string Message { get; set; }
    public string? ResponseBody { get; set; }
}

public sealed class ContentFilterError : AssistantError
{
    public required string Message { get; set; }
}

public sealed class ApiAssistantError : AssistantError
{
    public required string Message { get; set; }
    public double? StatusCode { get; set; }
    public bool IsRetryable { get; set; }
    public Dictionary<string, string>? ResponseHeaders { get; set; }
    public string? ResponseBody { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

#endregion

#region Session status payload types

public sealed class SessionStatusIdle : SessionStatusPayload { }

public sealed class SessionStatusBusy : SessionStatusPayload { }

public sealed class SessionStatusRetry : SessionStatusPayload
{
    public double Attempt { get; set; }
    public required string Message { get; set; }
    public double Next { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionStatusRetryAction? Action { get; set; }
}

public sealed class SessionStatusRetryAction
{
    public required string Reason { get; set; }
    public required string Provider { get; set; }
    public required string Title { get; set; }
    public required string Message { get; set; }
    public required string Label { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Link { get; set; }
}

#endregion

#region File part source types

public sealed class FileSource : FilePartSource
{
    public required FileSourceText Text { get; set; }
    public required string Path { get; set; }
}

public sealed class SymbolSource : FilePartSource
{
    public required FileSourceText Text { get; set; }
    public required string Path { get; set; }
    public required SourceRange Range { get; set; }
    public required string Name { get; set; }
    public double Kind { get; set; }
}

public sealed class ResourceSource : FilePartSource
{
    public required FileSourceText Text { get; set; }
    [JsonPropertyName("clientName")] public required string ClientName { get; set; }
    public required string Uri { get; set; }
}

#endregion

#region Tool content (V2 session.next.* tool events)

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ToolTextContent), "text")]
[JsonDerivedType(typeof(ToolFileContent), "file")]
public abstract class ToolContent { }

public sealed class ToolTextContent : ToolContent
{
    public required string Text { get; set; }
}

public sealed class ToolFileContent : ToolContent
{
    public required string Uri { get; set; }
    public required string Mime { get; set; }
    public string? Name { get; set; }
}

#endregion

#region Event type string constants

/// <summary>All SSE event type strings emitted by the opencode server.</summary>
public static class EventTypes
{
    // Server / lifecycle
    public const string ServerConnected = "server.connected";
    public const string ServerDisposed = "global.disposed";
    public const string ServerInstanceDisposed = "server.instance.disposed";

    // Session V1
    public const string SessionCreated = "session.created";
    public const string SessionUpdated = "session.updated";
    public const string SessionDeleted = "session.deleted";

    // Message V1
    public const string MessageUpdated = "message.updated";
    public const string MessageRemoved = "message.removed";
    public const string MessagePartUpdated = "message.part.updated";
    public const string MessagePartRemoved = "message.part.removed";
    public const string MessagePartDelta = "message.part.delta";

    // Session status
    public const string SessionStatus = "session.status";
    public const string SessionIdle = "session.idle";
    public const string SessionError = "session.error";
    public const string SessionDiff = "session.diff";
    public const string SessionCompacted = "session.compacted";

    // Permission V1
    public const string PermissionAsked = "permission.asked";
    public const string PermissionReplied = "permission.replied";

    // Permission V2
    public const string PermissionV2Asked = "permission.v2.asked";
    public const string PermissionV2Replied = "permission.v2.replied";

    // Question V1
    public const string QuestionAsked = "question.asked";
    public const string QuestionReplied = "question.replied";
    public const string QuestionRejected = "question.rejected";

    // Question V2
    public const string QuestionV2Asked = "question.v2.asked";
    public const string QuestionV2Replied = "question.v2.replied";
    public const string QuestionV2Rejected = "question.v2.rejected";

    // Session V2 (next) — agent/model
    public const string AgentSwitched = "session.next.agent.switched";
    public const string ModelSwitched = "session.next.model.switched";
    public const string Moved = "session.next.moved";

    // Session V2 — prompting
    public const string Prompted = "session.next.prompted";
    public const string PromptAdmitted = "session.next.prompt.admitted";
    public const string ContextUpdated = "session.next.context.updated";
    public const string Synthetic = "session.next.synthetic";

    // Session V2 — shell
    public const string ShellStarted = "session.next.shell.started";
    public const string ShellEnded = "session.next.shell.ended";

    // Session V2 — step
    public const string StepStarted = "session.next.step.started";
    public const string StepEnded = "session.next.step.ended";
    public const string StepFailed = "session.next.step.failed";

    // Session V2 — text streaming
    public const string TextStarted = "session.next.text.started";
    public const string TextDelta = "session.next.text.delta";
    public const string TextEnded = "session.next.text.ended";

    // Session V2 — reasoning
    public const string ReasoningStarted = "session.next.reasoning.started";
    public const string ReasoningDelta = "session.next.reasoning.delta";
    public const string ReasoningEnded = "session.next.reasoning.ended";

    // Session V2 — tool input
    public const string ToolInputStarted = "session.next.tool.input.started";
    public const string ToolInputDelta = "session.next.tool.input.delta";
    public const string ToolInputEnded = "session.next.tool.input.ended";

    // Session V2 — tool lifecycle
    public const string ToolCalled = "session.next.tool.called";
    public const string ToolProgress = "session.next.tool.progress";
    public const string ToolSuccess = "session.next.tool.success";
    public const string ToolFailed = "session.next.tool.failed";

    // Session V2 — retry
    public const string Retried = "session.next.retried";

    // Session V2 — compaction
    public const string CompactionStarted = "session.next.compaction.started";
    public const string CompactionDelta = "session.next.compaction.delta";
    public const string CompactionEnded = "session.next.compaction.ended";

    // Session V2 — revert
    public const string RevertStaged = "session.next.revert.staged";
    public const string RevertCleared = "session.next.revert.cleared";
    public const string RevertCommitted = "session.next.revert.committed";

    // File
    public const string FileEdited = "file.edited";
    public const string FileWatcherUpdated = "file.watcher.updated";

    // VCS
    public const string VcsBranchUpdated = "vcs.branch.updated";

    // Todo
    public const string TodoUpdated = "todo.updated";

    // Command
    public const string CommandExecuted = "command.executed";

    // MCP
    public const string McpToolsChanged = "mcp.tools.changed";
    public const string McpBrowserOpenFailed = "mcp.browser.open.failed";

    // TUI
    public const string TuiPromptAppend = "tui.prompt.append";
    public const string TuiCommandExecute = "tui.command.execute";
    public const string TuiToastShow = "tui.toast.show";
    public const string TuiSessionSelect = "tui.session.select";

    // Project
    public const string ProjectUpdated = "project.updated";
    public const string ProjectDirectoriesUpdated = "project.directories.updated";

    // PTY
    public const string PtyCreated = "pty.created";
    public const string PtyUpdated = "pty.updated";
    public const string PtyExited = "pty.exited";
    public const string PtyDeleted = "pty.deleted";

    // Reference / Plugin / Catalog
    public const string ReferenceUpdated = "reference.updated";
    public const string PluginAdded = "plugin.added";
    public const string CatalogUpdated = "catalog.updated";
    public const string ModelsDevRefreshed = "models-dev.refreshed";

    // Installation
    public const string InstallationUpdated = "installation.updated";
    public const string InstallationUpdateAvailable = "installation.update-available";

    // LSP
    public const string LspUpdated = "lsp.updated";

    // Integration
    public const string IntegrationUpdated = "integration.updated";
    public const string IntegrationConnectionUpdated = "integration.connection.updated";

    // Workspace
    public const string WorkspaceReady = "workspace.ready";
    public const string WorkspaceFailed = "workspace.failed";
    public const string WorkspaceStatus = "workspace.status";

    // Worktree
    public const string WorktreeReady = "worktree.ready";
    public const string WorktreeFailed = "worktree.failed";
}

#endregion
