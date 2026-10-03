using System.Text.Json.Serialization;

namespace UnoVibe.Integration.Events;

/// <summary>
/// Typed model for the <c>metadata</c> field on <see cref="ToolStateCompleted"/> and
/// <see cref="ToolStateError"/>. Each tool populates different subsets of these fields;
/// unused properties remain <c>null</c>. System.Text.Json ignores unknown properties
/// during deserialization, so extra server-side fields are silently dropped.
/// </summary>
public sealed class ToolMetadata
{
    // Shell (bash)
    public string? Output { get; set; }
    [JsonPropertyName("exit")] public long? ExitCode { get; set; }

    // Glob / Grep
    public long? Count { get; set; }
    public long? Matches { get; set; }

    // Read
    public string? Preview { get; set; }
    public List<string>? Loaded { get; set; }
    public ReadDisplay? Display { get; set; }

    // Edit / Apply Patch
    public string? Diff { get; set; }
    public FileDiffInfo? Filediff { get; set; }
    public Dictionary<string, List<LspDiagnostic>>? Diagnostics { get; set; }

    // Write
    public string? Filepath { get; set; }
    public bool? Exists { get; set; }

    // Apply Patch
    public List<ApplyPatchFileMeta>? Files { get; set; }

    // Task (subagent)
    [JsonPropertyName("parentSessionId")] public string? ParentSessionId { get; set; }
    [JsonPropertyName("sessionId")] public string? SessionId { get; set; }
    public TaskModelInfo? Model { get; set; }
    public bool? Background { get; set; }
    [JsonPropertyName("jobId")] public string? JobId { get; set; }

    // Question
    public List<List<string>>? Answers { get; set; }

    // Todo
    public List<TodoInfo>? Todos { get; set; }

    // Skill
    public string? Name { get; set; }
    public string? Dir { get; set; }

    // WebSearch
    public string? Provider { get; set; }

    // LSP
    public List<object>? Result { get; set; }

    // Truncation (injected by the tool wrapper for most tools)
    public bool? Truncated { get; set; }
    public string? OutputPath { get; set; }

    // Interrupted flag (set on tool error/abort)
    public bool? Interrupted { get; set; }
}

#region Supporting sub-models for ToolMetadata

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ReadFileDisplay), "file")]
[JsonDerivedType(typeof(ReadDirectoryDisplay), "directory")]
public abstract class ReadDisplay { }

public sealed class ReadFileDisplay : ReadDisplay
{
    public string Path { get; set; } = "";
    public string Text { get; set; } = "";
    [JsonPropertyName("lineStart")] public long LineStart { get; set; }
    [JsonPropertyName("lineEnd")] public long LineEnd { get; set; }
    public long TotalLines { get; set; }
    public bool Truncated { get; set; }
}

public sealed class ReadDirectoryDisplay : ReadDisplay
{
    public string Path { get; set; } = "";
    public List<string> Entries { get; set; } = [];
    public long Offset { get; set; }
    public long TotalEntries { get; set; }
    public bool Truncated { get; set; }
}

public sealed class FileDiffInfo
{
    public string? File { get; set; }
    public string? Patch { get; set; }
    public long Additions { get; set; }
    public long Deletions { get; set; }
    public string? Status { get; set; }
}

public sealed class LspDiagnostic
{
    public string? Message { get; set; }
    public string? Severity { get; set; }
    public LspRange? Range { get; set; }
}

public sealed class LspRange
{
    public LspPosition? Start { get; set; }
    public LspPosition? End { get; set; }
}

public sealed class LspPosition
{
    public long Line { get; set; }
    public long Character { get; set; }
}

public sealed class ApplyPatchFileMeta
{
    public string FilePath { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string Type { get; set; } = ""; // "add" | "update" | "delete" | "move"
    public string Patch { get; set; } = "";
    public long Additions { get; set; }
    public long Deletions { get; set; }
    public string? MovePath { get; set; }
}

public sealed class TaskModelInfo
{
    [JsonPropertyName("modelID")] public string ModelId { get; set; } = "";
    [JsonPropertyName("providerID")] public string ProviderId { get; set; } = "";
}

public sealed class TodoInfo
{
    public string Content { get; set; } = "";
    public string Status { get; set; } = "";
    public string Priority { get; set; } = "";
}

#endregion
