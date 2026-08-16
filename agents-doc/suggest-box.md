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
`SubmitRequested(sender, text)` on bare Enter (flyout closed) and offers `Clear()`; every other
property/event (`Text`, `PlaceholderText`, `MaxHeight`, `PreviewKeyDown`, ...) forwards to the inner
TextBox via `MarkupNode`.

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
as a defensive branch.

**No client-side command expansion:**
the server does NOT expand `/name args` at the REST layer — UnoVibe inserts `/name ` text and sends
it through the normal `SendAsync`/`prompt_async` path; the session loop resolves the command
server-side. Never read `Command.Info.template` from the REST list (it can serialize as a Promise
stub); `hints` (from `$1..$n`/`$ARGUMENTS`) exist but are unused.
The legacy route contract (bare arrays, full `source` field) vs the `/api/*` contract
(wrapped `{location, data}`) is documented in
[`opencode-server.md`](opencode-server.md) and in `OpencodeClient.cs` itself.