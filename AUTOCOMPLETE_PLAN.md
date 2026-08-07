# Chat Input Autocomplete — Phase 2 (real data) handoff

Implementation-ready plan for wiring the Phase 1 chat suggestion box to the opencode server's real
command/skill/filesystem data. Phase 1 (UI plumbing + mock providers) is done and building.

## What Phase 1 already delivered

- `UnoVibe/Controls/SuggestBox.cs` — self-contained QuickMarkup component (a plain-text
  `TextBox` + attached suggestion `Flyout`). Declares `Prefixes` (`"/@"`) and `Providers`
  (suggestion sources) as public members; raises `SubmitRequested(sender, text)` on bare Enter
  when the flyout is closed; offers `Clear()` for the host. All other properties/events
  (`Text`, `PlaceholderText`, `MaxHeight`, `PreviewKeyDown`, ...) forward to the inner `TextBox`
  via `MarkupNode`. Copy it with `SuggestionItem.cs` + `SuggestionBoxController.cs` to reuse in
  another QuickMarkup project.
- `UnoVibe/Controls/SuggestionItem.cs` — `{ Key, Kind, Text, Insert, Detail, KindLabel }`.
- `UnoVibe/Controls/SuggestionBoxController.cs` — `ISuggestionProvider` (one method:
  `Task<IReadOnlyList<SuggestionItem>> GetSuggestionsAsync(string query, CancellationToken ct)`)
  and the parsing/dispatch engine: `Prefixes` (default `"/@"`),
  `TryGetQuery(text, caret, out trigger, out query, out tokenStart)`, and
  `GetSuggestionsAsync(trigger, query)` which runs every provider registered for that trigger and
  merges+dedupes by `Key`. Rules: every prefix triggers at start-of-token (start of input or
  preceded by whitespace — so `/` works mid-sentence like `please help with /review-code`, and
  `@` works as `foo @file`; `foo/bar`/email `foo@bar` do not); query = text between trigger and
  caret (whitespace → no suggestion box). Items flagged `InputStartOnly` (built-in commands like
  `/new`) are dropped when the trigger isn't at position 0, so `/new` is position-0-only while
  skills (`/quickmarkup`) and `@file` work anywhere.
- `UnoVibe/Services/SuggestionProviders.cs` — mock providers implementing the above interface:
  `MockCommandSuggestionProvider` (5 fake commands) and `MockSkillSuggestionProvider` (2 fake skills).
  Filtering is a case-insensitive substring match on the item `Text`. Live in `namespace UnoVibe.Controls`.
- `UnoVibe/Pages/ChatPage.cs` — consumes `<SuggestBox Text<=>`Input` SubmitRequested+=`OnSubmitRequested` />`
  with the mock providers; keyboard/mouse UX, flyout, 60 ms debounce + `_suggestSeq` guard, and
  commit path (re-parse, replace `tokenStart..caret` with `item.Insert`, close, refocus) all moved
  inside the component.

**Do not regress Phase 1 behavior.** The task is to swap mock data for real server data and add an
`@file` provider — keyboard/mouse UX, flyout, debounce, and commit path stay as-is.

## The two big risks to verify first (by testing, since they're new server surface)

1. **The `/api/*` experimental endpoints are brand-new HttpApi surface** (`packages/protocol/src/groups/*`,
   `packages/opencode/src/server/routes/instance/httpapi/*`). Confirm the running server actually serves
   them (`curl -H "Authorization: Basic ..." http://localhost:4196/api/command`) before coding.
2. **Deep-object query encoding** — the location param is serialized as `location[directory]=...`
   (OpenAPI `style: deepObject, explode: true`). The generated client sends it via `qs` (brackets
   encoded as `%5B`/`%5D`). Either `location[directory]=...` or `location%5Bdirectory%5D=...` should
   parse, but verify with curl. The older instance routes (`/command`, `/skill` in
   `groups/instance.ts`) historically took plain `?directory=` (see `WorkspaceRoutingQueryFields`) —
   but the **generated client uses `location` deep-object for `/api/command` too**, so prefer the
   `location[directory]=` form for consistency; if it 400s, fall back to `?directory=`.

## Verified server contracts (opencode-src, current HEAD)

All endpoints require the same Basic auth as everything else (Authorization middleware).

### `GET /api/command`
Query: `location[directory]` (optional), `location[workspace]` (optional).
Response: bare array of `Command.Info`:
```
{ name: string, description?: string, agent?: string, model?: string, variant?: string,
  source?: "command"|"mcp"|"skill", template: unknown /* string or Promise — do NOT use from REST */,
  subtask?: boolean, hints: string[] }
```
Built-in `init` / `review` are included (`source: "command"`). Do not filter them out.
Source: `packages/opencode/src/command/index.ts` (Info at line 22; init/review at 70/79).

### `GET /api/skill`
Query: `location[directory]`, `location[workspace]`.
Response: bare array of `Skill.Info`:
```
{ name: string, description?: string, location: string, content: string }
```
Source: `packages/opencode/src/skill/index.ts` (Info at line 37).

### `GET /api/fs/list`
Query: `location[directory]`, `location[workspace]`, `path` (optional relative path).
Response wrapper:
```
{ location: { directory: string, workspaceID?: string, project: { id: string, directory: string } },
  data: [ { path: string, type: "file"|"directory" } ] }
```

### `GET /api/fs/find`
Query: `location[directory]`, `location[workspace]`, `query` (string), `type` ("file"|"directory"),
`limit` (1..200). Response: same wrapper as `fs/list`.
Source: `packages/protocol/src/groups/fs.ts`; entry/schema shapes in
`packages/schema/src/filesystem.ts` (`Entry = {path, type}`) and `packages/schema/src/location.ts`
(`Location.response` wrapper).

## TUI reference behavior (opencode-src/packages/tui/src/component/prompt/autocomplete.tsx)

This is the behavior to mirror. Key excerpts:

- **`/` trigger**: `value.startsWith("/") && !value.slice(0, offset).match(/\s/)` → show; hide when a
  whitespace appears between trigger and cursor, or when value matches `^\S+\s+\S+\s*$` (command args
  typed → close).
- **Slash options** (`commands` memo, lines 447–474): start from keymap slash commands (TUI-only —
  **skip these**, user explicitly wants only server/user-defined commands), then for each server
  command: `if (serverCommand.source === "skill") continue`, label MCP ones as `:mcp` suffix, display
  `"/" + name + label`, description as detail. Sorted by `display.localeCompare`. On select the whole
  input is replaced with `"/" + name + " "`.
  → Decide: the user wants skills under `/` too, but TUI excludes them (skills auto-load at session
  start). Recommended: keep them in the list (badge them as skills) but they can be dropped later.
- **`@` options**: reference aliases, then agents (`mode !== "primary" && !hidden`, insert `@name` as
  an `agent` part), then MCP resources, then files.
- **Files** (`files` resource, lines 316–364): `sdk.client.v2.fs.find({ query: baseQuery, limit: "20",
  location: { directory, workspace } })`. Server returns results pre-ranked (frecency, fuzzy score,
  filename bonus) — **do not re-sort**. Display `path`; directories flagged. On select insert
  `@path ` (trailing space only if char after cursor isn't a space). On "complete" for a directory:
  expand to `@path/` and keep the box open.
- **Line ranges**: `@file#12` and `@file#12-34` — strip the `#...` from the find query
  (`extractLineRange`). Files render `path#start[-end]`.
- **Search** (lines 502–524): fuzzysort on display/value; for `/` also match `description`; threshold
  0 for `/`, 0.5 for `@`; limit 10. Files are never re-filtered client-side — server already filtered.
- Server **does NOT expand `/name args` into the command template for you at the REST layer** — the
  TUI just sends the literal `/name ` text via `prompt_async` and the session loop resolves it
  (`src/session/prompt.ts`). So UnoVibe needs **no** extra send logic: `CommitSuggestion` inserting
  `/name ` and then a normal `ChatStore.SendAsync(text)` is correct. (Placeholders `$1..$n`/
  `$ARGUMENTS` are for templates with args typed after the command.)

## Implementation plan

1. **`UnoVibe/Models/`** — add small DTOs:
   - `ServerCommandItem` / `ServerSkillItem` — or reuse `SuggestionItem` directly from providers.
   - `FileSystemEntry`-like record `{ string Path; string Type }` for the fs wrapper
     `{ location, data }` (parse only `data`).
2. **`UnoVibe/Services/OpencodeClient.cs`** — add methods following the existing
   `JsonDocument` + `GetStringProperty` pattern (see `GetMcpStatusAsync` / `GetModesAsync`):
   - `GetCommandsAsync(string? directory = null, CancellationToken ct)` → `GET /api/command`
     with `location[directory]=<escaped>`; parse array of `{name, description, source, hints}`.
   - `GetSkillsAsync(string? directory = null, CancellationToken ct)` → `GET /api/skill`
     (parse `{name, description}`).
   - `FindFilesAsync(string query, string? directory, string? type = null, int limit = 20, ct)`
     → `GET /api/fs/find` with `query`, `type`, `limit`, and location; parse `data[]` entries.
   - `ListDirectoryAsync(string path, string? directory, ct)` → `GET /api/fs/list` (optional;
     only needed if you implement directory expansion via server-side listing rather than `fs/find`).
   - Add a small private helper to build the deep-object location query string:
     `$"?location[directory]={Uri.EscapeDataString(dir)}"` (and append `&location[workspace]=...`
     only when a workspace id is known — ChatStore does not currently track one; passing just
     `directory` is fine).
   - Guard every new method with a try/catch returning empty lists on `HttpRequestException` /
     non-success so the suggestion box degrades gracefully (mock providers as fallback).
3. **`UnoVibe/Services/SuggestionProviders.cs`** — replace/augment mocks:
   - `ServerCommandSuggestionProvider` — takes `OpencodeClient` + a `Func<string> directoryProvider`
     (wire to `ChatStore.ActiveDirectory()` — note it's `private`; expose a public accessor or pass
     the value from `ChatPage` each call). Maps each command to
     `SuggestionItem { Key = "cmd:"+name, Kind = Command, Text = name, Insert = "/"+name+" ",
     Detail = description }`. MCP entries: `Kind = Command`, `Insert = "/"+name+" "`; optionally
     append `:mcp` to `Text` for display parity (then `Insert` must NOT include `:mcp`). Skills from
     the command list (`source == "skill"`): `Kind = Skill`, `Insert = "/"+name+" "` (recommended;
     drop them entirely if we decide to mirror TUI's `continue`).
   - `ServerFileSuggestionProvider` (for `@`) — calls `FindFilesAsync(query, directory)`, maps each
     entry to `SuggestionItem { Key = "file:"+path, Kind = File, Text = path, Insert = "@"+path+" ",
     Detail = type }`. Case-insensitive filter is already applied server-side; do **not** re-filter,
     do **not** re-sort, respect `limit`. Directory entries → `Insert = "@"+path+"/"` so a second
     commit keeps browsing (optional nicety; Phase 1 commit closes the box, so either accept that or
     special-case).
   - Keep the mocks for offline/no-server fallback, or wire a single provider that returns mocks when
     the client/directory is unavailable.
4. **`SuggestionBoxController`** (in `UnoVibe/Controls/`) — already routes by trigger
   (`"/" → command provider(s)`, `"@" → file provider` via each provider's `Trigger`). Verify it passes
   the raw query through (no substring filtering in the controller) so `@` providers get the full query
   for the server to fuzzy-match. Adding a server provider is just an `ISuggestionProvider` with
   `Trigger = '@'`.
5. **`ChatPage.cs`** — wire real providers into `SuggestBox.Providers` in code-behind (the `Ctor`
   already owns the mock list), pulling the directory from `ChatStore` on each call (debounce already
   exists). No markup changes expected. The component's `CommitSuggestion` already inserts
   `item.Insert` — for `/` commands that yields `/name ` and a subsequent `SendAsync` round-trips the
   expansion server-side. Verify a leading-space commit still refocuses (existing behavior).
 6. **QA checklist** (manual, per AGENTS.md — do not launch/test the app yourself):
    - `/` at position 0 lists init, review, user commands; MCP entries carry `:mcp`; skills appear
      badge-skilled; typing filters; Enter/Tab inserts `/name `; typing a space closes the box.
    - `/` mid-sentence (e.g. `please help with /review-code`) lists skills/insertable commands but
      NOT position-0-only built-ins like `/new` (intentional divergence from the TUI, which only
      accepts `/` at position 0).
    - `@` in a session on a non-empty directory lists real files via `fs/find`; Enter inserts `@path `;
      `@foo` (mid-token, no whitespace) does NOT trigger, but `foo @file` does.
    - Send `/init` and confirm the server executes the init prompt (proves no client-side expansion
      needed).
    - With server down (or no `OPENCODE_BASE_URL`), the box still works with mock providers.

## Notes / caveats

- `Command.Info.template` is `Schema.Unknown` and can serialize as a Promise stub — never read it from
  the REST list. `hints` (from `$1..$n`/`$ARGUMENTS`) can seed a future "show placeholders" detail.
- Location `workspace` is only needed for workspace-v2 setups; UnoVibe uses plain directories, so
  omit it unless a session reports one.
- The old plain `/fs/list`, `/fs/find` routes found in earlier research **do not exist in current
  opencode-src** — the surface is `/api/fs/*`. Use the `/api/*` forms above.
- If `/api/command` 400s on the running server (risk 1), check the serve version:
  the HttpApi surface requires a recent CLI; update the dev server or pin behavior to the matching
  legacy routes (`/command` endpoint in the TUI SDK's generated client as the fallback reference).
- AGENTS.md must be updated once Phase 2 lands (this box now uses live server data).
