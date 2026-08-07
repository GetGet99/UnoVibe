namespace UnoVibe.Controls;

/// <summary>
/// Mock slash-command provider — demonstrates the "/" pipeline until the real
/// <c>GET /command</c> provider lands. Filtering is a case-insensitive substring match.
/// </summary>
public sealed class MockCommandSuggestionProvider : ISuggestionProvider
{
    public char Trigger => '/';
    public string Name => "mock-commands";

    private static readonly SuggestionItem[] All =
    {
        new() { Key = "cmd:new", Kind = "command", Text = "/new", Insert = "/new ", Detail = "Start a new chat", InputStartOnly = true },
        new() { Key = "cmd:compact", Kind = "command", Text = "/compact", Insert = "/compact ", Detail = "Compact the conversation history", InputStartOnly = true },
        new() { Key = "cmd:rename", Kind = "command", Text = "/rename", Insert = "/rename ", Detail = "Rename this session", InputStartOnly = true },
        new() { Key = "cmd:review", Kind = "command", Text = "/review", Insert = "/review ", Detail = "Review the current changes", InputStartOnly = true },
        new() { Key = "cmd:todo", Kind = "command", Text = "/todo", Insert = "/todo ", Detail = "Show the todo list", InputStartOnly = true },
    };

    public Task<IReadOnlyList<SuggestionItem>> GetSuggestionsAsync(string query, CancellationToken ct = default)
    {
        var filtered = All.Where(s => string.IsNullOrEmpty(query)
            || s.Text.Contains(query, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<IReadOnlyList<SuggestionItem>>(filtered.ToArray());
    }
}

/// <summary>
/// Mock skill provider — mirrors the real skills present in this repo so the "/" list shows
/// both kinds. The real implementation reads <c>GET /command</c> entries with
/// <c>source == "skill"</c>.
/// </summary>
public sealed class MockSkillSuggestionProvider : ISuggestionProvider
{
    public char Trigger => '/';
    public string Name => "mock-skills";

    private static readonly SuggestionItem[] All =
    {
        new() { Key = "skill:quickmarkup", Kind = "skill", Text = "/quickmarkup", Insert = "/quickmarkup ", Detail = "Write/edit QuickMarkup declarative UI markup" },
        new() { Key = "skill:customize-opencode", Kind = "skill", Text = "/customize-opencode", Insert = "/customize-opencode ", Detail = "Edit opencode's own configuration" },
    };

    public Task<IReadOnlyList<SuggestionItem>> GetSuggestionsAsync(string query, CancellationToken ct = default)
    {
        var filtered = All.Where(s => string.IsNullOrEmpty(query)
            || s.Text.Contains(query, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<IReadOnlyList<SuggestionItem>>(filtered.ToArray());
    }
}
