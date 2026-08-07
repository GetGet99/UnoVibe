namespace UnoVibe.Models;

/// <summary>One entry from <c>GET /api/command</c> (a server/user/MCP/skill command).</summary>
public sealed record ServerCommandItem
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    // "command" | "mcp" | "skill" (absent on some servers — default "command").
    public string Source { get; init; } = "";
    public bool Subtask { get; init; }
    public string[] Hints { get; init; } = Array.Empty<string>();
}

/// <summary>One entry from <c>GET /api/skill</c>.</summary>
public sealed record ServerSkillItem
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
}

/// <summary>One entry from <c>GET /api/fs/find</c> or <c>GET /api/fs/list</c>.</summary>
public sealed record FileSystemEntry
{
    public string Path { get; init; } = "";
    public string Type { get; init; } = "";
}
