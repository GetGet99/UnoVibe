namespace UnoVibe.Models;

public sealed record ServerCommandItem
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Source { get; init; } = "";
    public bool Subtask { get; init; }
    public string[] Hints { get; init; } = Array.Empty<string>();
}

public sealed record ServerSkillItem
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
}

public sealed record FileSystemEntry
{
    public string Path { get; init; } = "";
    public string Type { get; init; } = "";
}
