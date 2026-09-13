using UnoVibe.Integration;
namespace UnoVibe.States;

partial class ChatboxState
{
    // ── Slash-command send detection ─────────────────────────────────────────────
    // The composer routes "/name args" through POST /session/{id}/command (server expands the
    // template) instead of sending the text verbatim — the same check the TUI/web clients make
    // against their synced command list. The list is directory-scoped, so the cache is keyed to
    // ActiveDirectory() and invalidated by directory change or a short TTL (commands can be
    // added/edited on disk while connected).
    //
    // The list mixes the three command sources the server folds in (file/config commands and
    // built-ins with source "command", MCP prompts with "mcp", skills with "skill"), so names
    // are cached split by source. When the "Expand skills" setting (SettingsStore.ExpandSkills)
    // is off, skill-only names fall through to a plain prompt; a name backed by a real command
    // always routes (the server itself drops a skill whose name collides with a command —
    // command/index.ts adds skills only for names not already taken).

    private const long CommandCacheTtlMs = 5 * 60 * 1000;
    private HashSet<string>? _commandNames;
    private HashSet<string>? _skillNames;
    private long _commandNamesFetchedMs;

    /// <summary>
    /// True when <paramref name="name"/> (the input token after the leading <c>/</c>) should be
    /// routed as a command for the active directory: it is a server command/MCP prompt (always),
    /// or a skill when the "Expand skills" setting is on. Fetches/cache-refreshes the command
    /// list on a directory change or staleness; returns false when the server is unreachable so
    /// slash text degrades to a plain prompt.
    /// </summary>
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
