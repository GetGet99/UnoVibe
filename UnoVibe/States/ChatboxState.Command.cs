using UnoVibe.Integration;
namespace UnoVibe.States;

partial class ChatboxState
{

    private const long CommandCacheTtlMs = 5 * 60 * 1000;
    private HashSet<string>? _commandNames;
    private HashSet<string>? _skillNames;
    private long _commandNamesFetchedMs;

    private async Task<bool> IsKnownCommandAsync(string name)
    {
        if (name.Length == 0) return false;
        var directory = Sessions.ActiveSessionDirectory;
        var now = Environment.TickCount64;
        if (_commandNames is null || _skillNames is null || now - _commandNamesFetchedMs > CommandCacheTtlMs)
        {
            var commands = await Opencode.GetCommandsAsync(directory);
            _commandNames = new HashSet<string>(StringComparer.Ordinal);
            _skillNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var command in commands.GetDataOr(static () => []))
            {
                if (command.Source == "skill") _skillNames.Add(command.Name);
                else _commandNames.Add(command.Name);
            }
            _commandNamesFetchedMs = now;
        }
        if (_commandNames.Contains(name)) return true;
        return SettingsStore.ExpandSkills && _skillNames.Contains(name);
    }
}
