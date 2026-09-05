# Chat input suggestion box

Reference for `SuggestBox` and its suggestion providers.
**Read this file when** editing `UnoVibe/Controls/SuggestBox.cs`, `SuggestionBoxController.cs`,
`SuggestionItem.cs`, `Services/SuggestionProviders.cs`, or the `OpencodeClient` fetch helpers they
use. For the Uno TextBox key-processing quirk the focus management depends on, see
[`referenced-projects.md`](referenced-projects.md).

`SuggestBox` in `UnoVibe/Controls/SuggestBox.cs` — a self-contained QuickMarkup component
(multiline TextBox + attached suggestion `Flyout`) that offers `@` and `/` completions.

**Public API:**
`Prefixes` (default `"/@"`) and `Providers` (suggestion sources) are public members; it raises
`SubmitRequested(sender, text)` on bare Enter (flyout closed), `CommandTriggered(sender, item)` when
a built-in command row is committed, and offers `Clear()`; every other property/event (`Text`,
`PlaceholderText`, `MaxHeight`, `PreviewKeyDown`, ...) forwards to the inner TextBox via
`MarkupNode`.

**Focus management:**
The flyout uses `ShowMode=Transient` so it never steals focus on open, plus a `GotFocus` handler on
the flyout content that bounces focus back to the input (mirrors RichSuggestBox's
`SuggestionList_GotFocus`) — the editor keeps focus for the whole suggestion session.

**Parsing + dispatch:**
Parsing + dispatch live in `SuggestionBoxController` (`UnoVibe/Controls/`) — every prefix (both `/`
and `@`) triggers at start-of-token (start of input or preceded by whitespace), so
`foo /skill-name` and `foo @file` work, while `foo/bar`/email `foo@bar` do not; providers route per
trigger char. Items flagged `InputStartOnly` on the `SuggestionItem` (built-in whole-input commands
like `/new`) are filtered out unless the trigger is at position 0, so `/new` only appears when the
input starts with `/`, while skills (`/quickmarkup`) and mentions work anywhere.
Row model in `Controls/SuggestionItem.cs`, providers in `Services/SuggestionProviders.cs`
(`namespace UnoVibe.Controls`).

## App built-in commands

`BuiltInCommands` (in `Services/SuggestionProviders.cs`) is the app-level catalog — TUI parity with
opencode's own `/new`, `/models`, etc., plus UnoVibe-only ones. `BuiltInCommandSuggestionProvider`
serves it locally (no server round-trip): rows are kind `"builtin"` (gray "built-in" badge) with a
non-null `SuggestionItem.Action` id, and are `InputStartOnly`, so like server commands they appear
only when `/` is the first input character.

The provider takes an optional **availability predicate** (`Func<string, bool>`); `ChatComposer`
passes one that hides context-dependent rows — currently only `/interrupt`, which is offered just
while the active session is busy. Committing an unavailable one anyway still degrades gracefully
(warning toast or no-op).

**Commit runs the action instead of inserting text.** Tab, Enter, or a mouse click on a built-in row
clears the whole composer input and raises `SuggestBox.CommandTriggered`;
`ChatComposer.RunBuiltInCommandAsync` dispatches on the action id:

- `/agents` → opens the Mode combobox (`ComboBox.IsDropDownOpen`)
- `/connect` → `ProviderConnectDialog.ShowAsync(Store, XamlRoot)` (the shared entry point also used
  by the model picker's "Connect a provider…" row)
- `/continue` → raises `SendRequested("continue")` — the same literal user message as the end-of-chat
  ⟳ Continue card (`session-state.md`)
- `/editor` → `FolderLauncher.OpenInEditor(ActiveDirectory())` — same launch as the header's code
  icon; failures toast
- `/explorer` → `FolderLauncher.OpenInFileManager(ActiveDirectory())`
- `/fork` → `ChatStore.ForkFullSessionAsync()` — same full-session fork as the header's ⇆ button
- `/interrupt` → `SessionStore.InterruptAsync()` when busy; warning toast otherwise (contextually
  hidden in the flyout while idle)
- `/mcps` → `ChatStore.RequestMcpSection()` → `SessionSidebar.RevealMcpSectionAsync`: on compact
  windows first flips to the sidebar view, expands the MCP section (starting its status poll), and
  focuses the MCP toggle button
- `/models` → `ModelPicker.Open()`
- `/new` → `Store.NewSessionAsync(Store.ActiveDirectory())` — identical to the "+" button on the
  current session's directory group (lazy draft; created on first send)
- `/redo` → `ChatPage.RedoLastAsync()` → `SessionStore.RedoLastMessageAsync()` + scroll to end
- `/rename` → `ChatPage.BeginRename()` → `ChatHeader.BeginRename()`: flips the header title into
  its inline rename TextBox and focuses/selects it; warning toast when there is no session yet
- `/setting` → sets the injected `SettingsOpen` provided value to true — the same flag the sidebar's
  gear button uses, so MainPage shows its settings modal overlay
- `/terminal` → `FolderLauncher.OpenInTerminal(ActiveDirectory())`
- `/undo` → `ChatPage.UndoLastAsync()` → `SessionStore.UndoLastMessageAsync()` + restore the undone
  prompt into the composer + scroll to end
- `/variants` → opens the Variant combobox; warning toast when the model has no variants

**Submit interception:** typed text that *is* an exact built-in invocation (`/name`, optional ignored
arguments — TUI-style) is consumed by `ChatComposer.TryRunBuiltInTextAsync` in both submit paths
(bare Enter + send button), so `/new hello` runs the action rather than reaching the model verbatim.
This runs before `SendRequested`, so `SessionStore.ParseSlashCommand`/server routing never sees a
built-in name.

**Name collisions:** built-ins win. `ServerCommandSuggestionProvider` skips a server command whose
name matches a built-in (`BuiltInCommands.IsBuiltIn`) — mirroring how the server drops a skill whose
name is taken by a command.

**Not yet implemented** (the rest of the TUI's list, deferred — revisit when adding more):
- `/diff` — TUI shows a dialog of working-tree changes
- `/exit` — quits opencode (desktop app: close window?)
- `/help` — help/shortcut listing
- `/move` — moves the session to another directory
- `/sessions` — session switcher (sidebar already covers this)
- `/skills` — skill list (already surfaced under `/` as suggestions)
- `/status` — server/provider/MCP status summary
- `/themes` — theme picker

## Live server data

- `ServerCommandSuggestionProvider`: legacy `GET /command?directory=` first, falling back to
  `GET /api/command?location[directory]=`; maps MCP entries with a ` :mcp` display suffix,
  `source == "skill"` → skill kind; commands/MCP are `InputStartOnly` so they only show when `/`
  is the first char — TUI parity.
- `ServerSkillSuggestionProvider`: legacy `GET /skill?directory=` first, falling back to
  `GET /api/skill?location[directory]=`; skills are insertable anywhere.
- `ServerFileSuggestionProvider`: `Trigger = '@'`, `GET /api/fs/find` only — the legacy `/fs/find`
  route 404s, verified.

All three take `Func<OpencodeClient?> client` + `Func<string> directory` (wired to `Store.Client` +
`Store.ActiveDirectory()` — the store's `Client` accessor and `ActiveDirectory()` are public).

**No mock fallback** (mock providers were deleted on request): a null client, unreachable server,
or empty response yields an empty list and the flyout closes.

**Route skew — why the legacy routes are primary:**
on the running dev server (opencode **1.17.18**), the legacy `/command?directory=`/
`/skill?directory=` routes return the FULL data (project skills like `quickmarkup`, MCP entries,
user commands, and the `source` field — bare arrays, no wrapper), while the newer `/api/*` HttpApi
surface returns only built-ins (`init`/`review`; built-in skill only) with `source` omitted.
The client therefore tries legacy first and keeps `/api/*` (wrapped `{location, data}`) as a fallback
for servers that drop the legacy routes; `OpencodeClient.FetchItemArrayAsync` accepts both a bare
array and the `data` envelope.
Skills appear from both providers and are deduped by `Key` (`skill:<name>`) in
`SuggestionBoxController`. The `:mcp` suffix is display-only (never inserted), matching the TUI's
`commands` memo (`autocomplete.tsx`).
`OpencodeClient.GetCommandsAsync`/`GetSkillsAsync`/`FindFilesAsync` parse the item arrays; the
deep-object location param is sent as `location%5Bdirectory%5D=<escaped>`.
The current dev server is 1.17.18 — older than the opencode-src checkout (1.18.11) whose
`/api/command` handler folds skills in via `Command.state`, so the `source == "skill"` mapping stays
as a defensive branch. (Note: this fact is no longer true as user now checks out on 1.17.18)

**Slash-command send (opencode Commands):**
the server does NOT expand `/name args` inside a normal prompt — a verbatim `/name` text would reach
the model unexpanded. So `SessionStore.SendPromptNowAsync` detects the command client-side
(`ParseSlashCommand`, mirroring the TUI's `prompt/index.tsx`: first line, first space-delimited token,
leading `/` stripped, remaining tokens join the arguments) and, when `Router.IsKnownCommandAsync`
matches a name from the server's command list for the active directory, fires
`OpencodeClient.SendCommandAsync` → legacy `POST /session/{id}/command` with
`{ command, arguments, agent?, model: "providerID/modelID", variant?, parts? }`. The server expands
the command's template (`$ARGUMENTS`/`$1..n`, `!`shell`, `@file`), resolves the command's own
agent/model/subtask options, and runs the turn. The endpoint **blocks until the turn completes**, so
the call uses a dedicated `HttpClient` with `Timeout.InfiniteTimeSpan` (the shared client's 100s
default would abort long commands) and is **fire-and-forget** — progress comes entirely over the SSE
stream (the response mirrors `prompt_async` events and is ignored), and the composer clears
immediately. The command-name cache in `ChatStore` is invalidated on directory change or a 5-minute
TTL, so edits to `commands/` show up without restarting the app (the server itself needs a restart to
reload them — see "Known Working State"). Never read `Command.Info.template` from the REST list (it
can serialize as a Promise stub); `hints` (from `$1..$n`/`$ARGUMENTS`) exist but are unused.
The legacy route contract (bare arrays, full `source` field) vs the `/api/*` contract
(wrapped `{location, data}`) is documented in
[`opencode-server.md`](opencode-server.md) and in `OpencodeClient.cs` itself.

**Skills expand through the same endpoint** — the server folds skills into the command list with
`source == "skill"` and runs `/name` (skill) through the exact same `POST /session/{id}/command`
handler, and it drops a skill whose name collides with a built-in/config/MCP command
(`command/index.ts` adds skills only for names not already taken), so a command always wins a
name conflict. UnoVibe's **Expand skills via slash commands** setting
(`SettingsStore.ExpandSkills`, default on = TUI behavior) only affects skill-only names:
`ChatStore.IsKnownCommandAsync` caches the command list split into real-command names vs
skill names, returns true for a real command regardless of the setting, and consults the toggle
before treating a skill-only name as a command — with the toggle off such text falls through to a
plain prompt. Arguments: `SessionStore.ParseSlashCommand` mirrors the TUI — first line, first
space-delimited token after `/` is the name, the rest of the line is space-joined **with empty
tokens preserved** (no quote/escape parsing — `"quoted args"` stay literal), plus any trailing
lines. The server then does its own quote-aware tokenizing for `$1..$n` placeholders
(`/"'[^"']*"'/` tokens, leading/trailing quote trimmed — backslash escapes like `\"` are NOT
understood) while `$ARGUMENTS` and the no-placeholder fallback append the raw arguments verbatim.