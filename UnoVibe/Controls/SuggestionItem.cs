namespace UnoVibe.Controls;

sealed class SuggestionItem
{
    public required string Key { get; init; }

    public required string Kind { get; init; }

    public required string Text { get; init; }

    public required string Insert { get; init; }

    public string Detail { get; init; } = "";

    public bool InputStartOnly { get; init; } = false;

    public string? Action { get; init; }

    public string KindLabel => Kind switch
    {
        "skill" => "skill",
        "file" => "file",
        "agent" => "agent",
        "builtin" => "built-in",
        _ => "cmd",
    };
}
