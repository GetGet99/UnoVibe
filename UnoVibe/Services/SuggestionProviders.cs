using UnoVibe.Services;

namespace UnoVibe.Controls;

/// <summary>Shared case-insensitive substring filter used by the server providers.</summary>
internal static class SuggestionFilter
{
    public static SuggestionItem[] Filter(IReadOnlyList<SuggestionItem> items, string query)
    {
        if (string.IsNullOrEmpty(query)) return items.ToArray();
        return items.Where(s => s.Text.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
}

/// <summary>
/// Server slash-command provider — lists every command the opencode server knows for the active
/// directory (legacy <c>GET /command?directory=</c>, falling back to <c>GET /api/command</c>):
/// built-ins (<c>init</c>/<c>review</c>), user-defined commands, MCP prompts, and any skill entries
/// the server folds in (<c>source == "skill"</c>). Mirrors the TUI's command list
/// (autocomplete.tsx <c>commands</c>): MCP entries get a <c>:mcp</c> display suffix only (the insert
/// stays clean <c>/name </c>); skills are kept under <c>/</c> as a deliberate UnoVibe extra (the TUI
/// skips them). Returns an empty list when the server is unreachable or returns nothing — the box
/// then simply shows no suggestions. Commands are <see cref="SuggestionItem.InputStartOnly"/> so they
/// only appear when <c>/</c> is the first character, like the TUI.
/// </summary>
public sealed class ServerCommandSuggestionProvider : ISuggestionProvider
{
    public char Trigger => '/';
    public string Name => "server-commands";

    private readonly Func<OpencodeClient?> _client;
    private readonly Func<string> _directory;

    public ServerCommandSuggestionProvider(Func<OpencodeClient?> client, Func<string> directory)
    {
        _client = client;
        _directory = directory;
    }

    public async Task<IReadOnlyList<SuggestionItem>> GetSuggestionsAsync(string query,
        CancellationToken ct = default)
    {
        try
        {
            var client = _client();
            if (client is null) return Array.Empty<SuggestionItem>();

            var commands = await client.GetCommandsAsync(_directory(), ct);
            if (commands.Count == 0) return Array.Empty<SuggestionItem>();

            var items = new List<SuggestionItem>(commands.Count);
            foreach (var command in commands)
            {
                var isSkill = command.Source == "skill";
                var isMcp = command.Source == "mcp";
                var display = "/" + command.Name;
                if (isMcp) display += " :mcp";
                items.Add(new SuggestionItem
                {
                    Key = isSkill ? $"skill:{command.Name}" : $"cmd:{command.Name}",
                    Kind = isSkill ? "skill" : "command",
                    Text = display,
                    Insert = "/" + command.Name + " ",
                    Detail = command.Description,
                    InputStartOnly = !isSkill,
                });
            }
            return SuggestionFilter.Filter(items, query);
        }
        catch
        {
            return Array.Empty<SuggestionItem>();
        }
    }
}

/// <summary>
/// Server skill provider — lists skills for the active directory (legacy <c>GET /skill?directory=</c>,
/// falling back to <c>GET /api/skill</c>). Returns an empty list when the server is unreachable or
/// returns nothing — the box then simply shows no suggestions. Skills are insertable anywhere (not
/// <see cref="SuggestionItem.InputStartOnly"/>), matching the old mock behavior.
/// </summary>
public sealed class ServerSkillSuggestionProvider : ISuggestionProvider
{
    public char Trigger => '/';
    public string Name => "server-skills";

    private readonly Func<OpencodeClient?> _client;
    private readonly Func<string> _directory;

    public ServerSkillSuggestionProvider(Func<OpencodeClient?> client, Func<string> directory)
    {
        _client = client;
        _directory = directory;
    }

    public async Task<IReadOnlyList<SuggestionItem>> GetSuggestionsAsync(string query,
        CancellationToken ct = default)
    {
        try
        {
            var client = _client();
            if (client is null) return Array.Empty<SuggestionItem>();

            var skills = await client.GetSkillsAsync(_directory(), ct);
            if (skills.Count == 0) return Array.Empty<SuggestionItem>();

            var items = skills.Select(skill => new SuggestionItem
            {
                Key = $"skill:{skill.Name}",
                Kind = "skill",
                Text = "/" + skill.Name,
                Insert = "/" + skill.Name + " ",
                Detail = skill.Description,
                InputStartOnly = false,
            }).ToList();
            return SuggestionFilter.Filter(items, query);
        }
        catch
        {
            return Array.Empty<SuggestionItem>();
        }
    }
}

/// <summary>
/// Server file provider (<c>@</c>) — fuzzy file search via <c>GET /api/fs/find</c>. The server
/// pre-filters and pre-ranks results, so results are NOT re-sorted or re-filtered here. Directories
/// insert a trailing slash so a follow-up commit keeps browsing into them. Empty list when the server
/// is unreachable or returns nothing.
/// </summary>
public sealed class ServerFileSuggestionProvider : ISuggestionProvider
{
    public char Trigger => '@';
    public string Name => "server-files";

    private readonly Func<OpencodeClient?> _client;
    private readonly Func<string> _directory;

    public ServerFileSuggestionProvider(Func<OpencodeClient?> client, Func<string> directory)
    {
        _client = client;
        _directory = directory;
    }

    public async Task<IReadOnlyList<SuggestionItem>> GetSuggestionsAsync(string query,
        CancellationToken ct = default)
    {
        try
        {
            var client = _client();
            if (client is null) return Array.Empty<SuggestionItem>();

            var entries = await client.FindFilesAsync(query, _directory(), ct: ct);
            var items = entries.Select(entry =>
            {
                var isDirectory = entry.Type == "directory";
                return new SuggestionItem
                {
                    Key = $"file:{entry.Path}",
                    Kind = "file",
                    Text = entry.Path + (isDirectory ? "/" : ""),
                    Insert = "@" + entry.Path + (isDirectory ? "/" : " "),
                    Detail = isDirectory ? "directory" : "",
                };
            }).ToList();
            return items;
        }
        catch
        {
            return Array.Empty<SuggestionItem>();
        }
    }
}
