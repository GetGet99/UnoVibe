namespace UnoVibe.Controls;

/// <summary>
/// A single row in the suggestion flyout of <see cref="SuggestBox"/>. <see cref="Kind"/> drives the
/// badge and color so the UI can tell commands, skills, files and agents apart at a glance.
/// </summary>
public sealed class SuggestionItem
{
    /// <summary>Stable identity used by QuickMarkup's foreach as the row key (e.g. "cmd:new", "skill:quickmarkup").</summary>
    public required string Key { get; init; }

    /// <summary>Suggestion category: "command", "skill", "file" or "agent".</summary>
    public required string Kind { get; init; }

    /// <summary>Display text, e.g. "/new" or "@src/Program.cs".</summary>
    public required string Text { get; init; }

    /// <summary>Literal text inserted at the token when committed (usually includes a trailing space).</summary>
    public required string Insert { get; init; }

    /// <summary>Secondary description line (command description / file path context).</summary>
    public string Detail { get; init; } = "";

    /// <summary>
    /// When true, this suggestion is only offered while the trigger token is at the very start of the
    /// input (position 0). Built-in whole-input commands like <c>/new</c> set this; insertable items
    /// like skills (<c>/quickmarkup</c>) or file mentions (<c>@path</c>) leave it false so they work
    /// anywhere in a sentence. The controller drops these items when the trigger isn't at position 0.
    /// </summary>
    public bool InputStartOnly { get; init; } = false;

    /// <summary>Short badge label shown next to the row text.</summary>
    public string KindLabel => Kind switch
    {
        "skill" => "skill",
        "file" => "file",
        "agent" => "agent",
        _ => "cmd",
    };
}
