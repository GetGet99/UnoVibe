# Chat input suggestion box

Reference for `SuggestBox` and its suggestion providers.
**Read this file when** editing `UnoVibe/Controls/SuggestBox.cs`, `SuggestionBoxController.cs`,
`SuggestionItem.cs`, `Commands/SuggestionProviders.cs`, or the `OpencodeClient` fetch helpers they
use. For the Uno TextBox key-processing quirk the focus management depends on, see
[`referenced-projects.md`](referenced-projects.md).

`SuggestBox` (`UnoVibe/Controls/SuggestBox.cs`) is a self-contained QuickMarkup component
(multiline TextBox + attached suggestion `Flyout`) offering `@` and `/` completions.
Public surface: `Prefixes` (default `"/@"`), `Providers`, `Text`,
`SubmitRequested`/`CommandTriggered` events, `RestoreDraft()`, `Clear()`,
`ClearAfterSubmit()`. Read and write the input only through those members —
never reach into `SuggestBox.MarkupNode.Text` directly.
The flyout never steals focus (`ShowMode=Transient` + focus bounce-back),
so the editor keeps focus for the whole suggestion session.

**Programmatic text: pick the method by intent.**
Setting the input falls into two cases with different stray-Enter needs
(Uno delivers a handled Enter as a newline anyway — see
[`referenced-projects.md`](referenced-projects.md)), so they are separate methods:
- Draft restore (`Text` setter, `RestoreDraft()`, plain `Clear()`): writes the text
  as-is, parks the caret at the end, and closes the flyout. No `AcceptsReturn`
  toggling — flipping the box to single-line while assigning multiline text clips it
  to one line. Used for session-switch draft swap, revert/fork restore, and mode switches.
- Post-submit clear (`ClearAfterSubmit()`): additionally arms the stray-Enter guards
  (`BeforeTextChanging` cancel + a brief `AcceptsReturn=false` pulse) so the Enter that
  submitted cannot land a newline in the emptied box. Used after every send/submit path
  (bare Enter, send button, shell submit, built-in command commit).
Programmatic writes never trigger a suggestion fetch (the `TextChanged` handler ignores
them and just closes the flyout).

**Parsing + dispatch** (`SuggestionBoxController`): every prefix triggers at start-of-token,
so `foo /skill` works while `foo/bar` and `foo@bar` do not. `InputStartOnly` items (whole-input
commands like `/new`) are filtered out unless the trigger is at position 0.

## App built-in commands

`BuiltInCommands` is the app-level catalog (TUI parity plus UnoVibe-only ones), served locally
with no server round-trip. Rows are kind `"builtin"` with a non-null action id and are
`InputStartOnly`. The provider takes an optional availability predicate (used to hide
context-dependent rows such as `/interrupt` while idle; committing one anyway degrades
gracefully).

**Commit runs the action instead of inserting text** (Tab/Enter/click clears the composer and
raises `CommandTriggered`; `ChatComposer` dispatches on the action id — see its
`RunBuiltInCommandAsync`). Available built-ins: `/agents` `/connect` `/continue` `/editor`
`/explorer` `/fork` `/interrupt` `/mcps` `/models` `/new` `/redo` `/rename` `/setting`
`/terminal` `/undo` `/variants`.

**Submit interception:** typed text that *is* an exact built-in invocation (`/name`, optional ignored
arguments — TUI-style) is consumed by `ChatComposer.TryRunBuiltInTextAsync` in both submit paths
(bare Enter + send button), so `/new hello` runs the action rather than reaching the model verbatim.
This runs before `SendRequested`, so `ChatboxState.ParseSlashCommand`/server routing never sees a
built-in name.

**Name collisions:** built-ins win. `ServerCommandSuggestionProvider` skips a server command whose
name matches a built-in (`BuiltInCommands.IsBuiltIn`) — mirroring how the server drops a skill whose
name is taken by a command.

**Not yet implemented** (deferred TUI rows — revisit when adding more):
`/diff` `/exit` `/help` `/move` `/sessions` `/skills` `/status` `/themes`.

## Live server data

- `ServerCommandSuggestionProvider`: legacy `GET /command?directory=` first, falling back to
  `GET /api/command?location[directory]=`; maps MCP entries with a ` :mcp` display suffix,
  `source == "skill"` → skill kind; commands/MCP are `InputStartOnly` so they only show when `/`
  is the first char — TUI parity.
- `ServerSkillSuggestionProvider`: legacy `GET /skill?directory=` first, falling back to
  `GET /api/skill?location[directory]=`; skills are insertable anywhere.
- `ServerFileSuggestionProvider`: `Trigger = '@'`, `GET /api/fs/find` only — the legacy `/fs/find`
  route 404s, verified.

All three take `Func<OpencodeClient?> client` + `Func<string> directory` (wired to `SessionsStateProvider.Client` +
`SessionsStateProvider.ActiveDirectory()` — the provider's `Client` accessor and `ActiveDirectory()` are public).

**No mock fallback** (mock providers were deleted on request): a null client, unreachable server,
or empty response yields an empty list and the flyout closes.

**Route skew — why the legacy routes are primary:**
on the running dev server the legacy `/command?directory=`/`/skill?directory=` routes return the
FULL data (project skills, MCP entries, user commands, `source` field — bare arrays, no wrapper),
while the newer `/api/*` surface returns only built-ins with `source` omitted.
The client therefore tries legacy first and keeps `/api/*` (wrapped `{location, data}`) as a
fallback; `FetchItemArrayAsync` accepts both a bare array and the `data` envelope.
Skills appear from both providers and are deduped by `Key` (`skill:<name>`).
The `:mcp` suffix is display-only (never inserted). The deep-object location param is sent as
`location%5Bdirectory%5D=<escaped>`.

**Slash-command send (opencode Commands):**
the server does NOT expand `/name args` inside a normal prompt, so the client detects commands
(first line, first space-delimited token, leading `/` stripped — TUI parity) and, when the name
matches the server's command list for the active directory, fires
`POST /session/{id}/command` with `{ command, arguments, agent?, model, variant?, parts? }`.
The server expands the template and runs the turn. The endpoint **blocks until the turn
completes**, so the call uses a dedicated no-timeout client and is **fire-and-forget** —
progress comes entirely over SSE and the composer clears immediately. The command-name cache
is invalidated on directory change or a 5-minute TTL. Never read `Command.Info.template` from
the REST list (it can serialize as a Promise stub).

**Skills expand through the same endpoint** — the server folds skills into the command list and
drops a skill whose name collides with a command, so a command always wins. The
**Expand skills via slash commands** setting (default on = TUI behavior) only affects skill-only
names: with it off such text falls through to a plain prompt. Argument parsing mirrors the TUI
(no quote/escape parsing); the server does its own quote-aware tokenizing for `$1..$n`.