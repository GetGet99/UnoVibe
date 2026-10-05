using UnoVibe.Integration;

namespace UnoVibe.Controls;

internal static class SuggestionFilter
{
    public static SuggestionItem[] Filter(IReadOnlyList<SuggestionItem> items, string query)
    {
        if (string.IsNullOrEmpty(query)) return items.ToArray();
        return items.Where(s => s.Text.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
}

public sealed record BuiltInCommand(string Name, string Description);

static class BuiltInCommands
{
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

    public static BuiltInCommand? Find(string name) =>
        All.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

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

    public static bool IsBuiltIn(string name) => Find(name) is not null;
}

sealed class BuiltInCommandSuggestionProvider : ISuggestionProvider
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

sealed class ServerCommandSuggestionProvider : ISuggestionProvider
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

            if (!(await client.GetCommandsAsync(_directory(), ct)).TryGetData(out var commands))
                return Array.Empty<SuggestionItem>();
            if (commands.Count == 0) return Array.Empty<SuggestionItem>();

            var items = new List<SuggestionItem>(commands.Count);
            foreach (var command in commands)
            {
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

sealed class ServerSkillSuggestionProvider : ISuggestionProvider
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

            if (!(await client.GetSkillsAsync(_directory(), ct)).TryGetData(out var skills))
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

sealed class ServerFileSuggestionProvider : ISuggestionProvider
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

            if (!(await client.FindFilesAsync(query, _directory(), ct: ct)).TryGetData(out var entries))
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
