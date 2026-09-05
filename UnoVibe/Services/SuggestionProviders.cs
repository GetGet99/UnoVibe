using UnoVibe.Integration;

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
/// An app-level built-in slash command (TUI parity where opencode has an equivalent —
/// <c>/new</c>, <c>/models</c>, ... — plus UnoVibe-only ones like <c>/explorer</c>):
/// discovered like server commands (only when <c>/</c> is the first input character) but executed
/// entirely client-side. Committing one clears the composer and runs the action — it never inserts
/// text or reaches the model.
/// </summary>
public sealed record BuiltInCommand(string Name, string Description);

/// <summary>
/// The built-in command catalog plus parsing helpers. The TUI's remaining built-ins
/// (/diff /exit /help /move /sessions /skills /status /themes) are documented as not yet
/// implemented in agents-doc/suggest-box.md.
/// </summary>
public static class BuiltInCommands
{
    /// <summary>The catalog shown by the suggestion flyout (alphabetical).</summary>
    public static readonly IReadOnlyList<BuiltInCommand> All = new BuiltInCommand[]
    {
        new("agents", "Open the agent/mode picker"),
        new("connect", "Connect a provider (API key or OAuth)"),
        new("continue", "Resume the turn by sending a \"continue\" message"),
        new("editor", "Open the current folder in your editor"),
        new("explorer", "Open the current folder in the file manager"),
        new("fork", "Fork this conversation into a new session"),
        new("interrupt", "Interrupt the running conversation"),
        new("mcps", "Show MCP servers in the sidebar"),
        new("models", "Open the model picker"),
        new("new", "Start a new chat in the current directory"),
        new("redo", "Restore reverted messages"),
        new("rename", "Rename this conversation"),
        new("setting", "Open the settings panel"),
        new("terminal", "Open the current folder in a terminal"),
        new("undo", "Undo the last exchange (prompt + reply)"),
        new("variants", "Open the reasoning-variant picker"),
    };

    /// <summary>Case-insensitive lookup by name (without the leading slash).</summary>
    public static BuiltInCommand? Find(string name) =>
        All.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Parses submitted composer text as a built-in command: the first token of the first line must
    /// be <c>/name</c> for a catalog name (arguments after it are allowed but ignored, TUI-style).
    /// Returns false when the text is not an exact built-in command invocation, so ordinary prompts
    /// and server commands are unaffected.
    /// </summary>
    public static bool TryParse(string? text, out BuiltInCommand command)
    {
        command = default!;
        var trimmed = text?.TrimStart();
        if (trimmed is null || !trimmed.StartsWith('/')) return false;
        var token = trimmed.Split(new[] { ' ', '\t', '\n', '\r' }, 2)[0];
        var found = Find(token.Substring(1));
        if (found is null) return false;
        command = found;
        return true;
    }

    /// <summary>True when a server-side command name would collide with a built-in (built-in wins).</summary>
    public static bool IsBuiltIn(string name) => Find(name) is not null;
}

/// <summary>
/// App built-in slash-command provider (<see cref="BuiltInCommands"/>). Local data — no server
/// round-trip. Rows are kind "builtin" with a non-null <see cref="SuggestionItem.Action"/> id and
/// are <see cref="SuggestionItem.InputStartOnly"/>, so they appear only when <c>/</c> is the first
/// character; committing clears the input and runs the action instead of inserting text.
/// An optional availability predicate hides commands that make no sense right now (e.g.
/// <c>/interrupt</c> only while the active session is busy) — committing one that slipped in
/// anyway still degrades gracefully (warning toast / no-op).
/// </summary>
public sealed class BuiltInCommandSuggestionProvider : ISuggestionProvider
{
    private readonly Func<string, bool>? _isAvailable;

    public BuiltInCommandSuggestionProvider(Func<string, bool>? isAvailable = null) =>
        _isAvailable = isAvailable;

    public char Trigger => '/';
    public string Name => "built-in-commands";

    public Task<IReadOnlyList<SuggestionItem>> GetSuggestionsAsync(string query,
        CancellationToken ct = default)
    {
        IReadOnlyList<SuggestionItem> items = BuiltInCommands.All
            .Where(command => _isAvailable?.Invoke(command.Name) ?? true)
            .Select(command => new SuggestionItem
        {
            Key = $"builtin:{command.Name}",
            Kind = "builtin",
            Text = "/" + command.Name,
            Insert = "",
            Detail = command.Description,
            InputStartOnly = true,
            Action = command.Name,
        }).ToArray();
        return Task.FromResult<IReadOnlyList<SuggestionItem>>(SuggestionFilter.Filter(items, query));
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

            if (!(await client.GetCommandsAsync(_directory(), ct)).TryGetValue(out var commands))
                return Array.Empty<SuggestionItem>();
            if (commands.Count == 0) return Array.Empty<SuggestionItem>();

            var items = new List<SuggestionItem>(commands.Count);
            foreach (var command in commands)
            {
                // Built-ins own their names client-side (same rule as the server dropping a skill
                // whose name is taken), so "/new" always runs the app action, never a user command.
                if (BuiltInCommands.IsBuiltIn(command.Name)) continue;
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
                    Detail = command.Description ?? "",
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
            if (client is null) return [];

            if (!(await client.GetSkillsAsync(_directory(), ct)).TryGetValue(out var skills))
                return [];
            if (skills.Count == 0) return [];

            var items = skills.Select(skill => new SuggestionItem
            {
                Key = $"skill:{skill.Name}",
                Kind = "skill",
                Text = "/" + skill.Name,
                Insert = "/" + skill.Name + " ",
                Detail = skill.Description ?? "",
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
            if (client is null) return [];

            if (!(await client.FindFilesAsync(query, _directory(), ct: ct)).TryGetValue(out var entries))
                return [];
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
            return [];
        }
    }
}
