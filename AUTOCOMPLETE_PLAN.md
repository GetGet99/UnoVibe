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

## The two big risks (both verified working on the dev server at `http://localhost:4196`)

1. **Route surface version skew** — the running dev server (opencode **1.17.18**) serves BOTH the
   legacy instance routes (`/command?directory=`, `/skill?directory=`) and the newer `/api/*` HttpApi
   surface — but they return **different data**:
   - **Legacy** `/command?directory=` → bare array, FULL list: built-ins (`init`, `review`), user
     commands (`test-add`), MCP entries (`uno-platform:init` etc.), **and project skills folded in**
     (`customize-opencode`, `quickmarkup`) — every item carries `source` (`"command"|"mcp"|"skill"`).
   - **Legacy** `/skill?directory=` → bare array with project skills (`['customize-opencode',
     'quickmarkup']`).
   - **HttpApi** `/api/command?location[directory]=` → wrapped `{location, data}` but only built-ins
     (`['init','review']`) — NO user commands, NO MCP, NO skills, and `source` omitted.
   - **HttpApi** `/api/skill?location[directory]=` → wrapped, only the built-in skill
     (`['customize-opencode']`) — project `.agents` skills are missing.
   → **This is why `/quickmarkup` never appeared in autocomplete**: the Phase-2 code hit only the
   `/api/*` surface, whose 1.17.x handler returns a non-empty-but-stub list, so the empty-list mock
   fallback never fired. The fix: try the legacy `/command`/`/skill` routes FIRST (verified complete
   on 1.17.18), falling back to `/api/*` for servers that drop the legacy routes.
2. **Deep-object query encoding** — the location param is serialized as `location[directory]=...`
   (OpenAPI `style: deepObject, explode: true`). The generated client sends it via `qs` (brackets
   encoded as `%5B`/`%5D`). **Confirmed**: both `location[directory]=...` and `location%5Bdirectory%5D=...`
   parse. UnoVibe sends the percent-encoded form (`location%5Bdirectory%5D=...`).

## Verified server contracts (opencode-src, current HEAD)

All endpoints require the same Basic auth as everything else (Authorization middleware).

### `GET /command?directory=` (legacy, PRIMARY for UnoVibe)
Query: `directory` (optional; defaults to the server's cwd / `x-opencode-directory` header).
Response: **bare array** of `Command.Info` (no wrapper) — the full list including user commands, MCP
entries, and skills folded in, each with `source`:
```
[ { name: string, description?: string, agent?: string, model?: string, variant?: string,
    source: "command"|"mcp"|"skill", subtask?: boolean, hints: string[] } ]
```
Confirmed on the 1.17.18 dev server for `/mnt/Data/Codes/UnoVibe`: 7 items (`init`, `review`,
`test-add`, `uno-platform:init`, `uno-platform:new`, `customize-opencode`, `quickmarkup`), all with
`source` present. The equivalent HttpApi surface is `GET /api/command?location[directory]=` (wrapped
`{location, data}`, same `data` item shape); on 1.17.x that surface omits `source` and returns only
built-ins, so a missing `source` is treated as `"command"` defensively.
Source: `packages/opencode/src/command/index.ts` (Info at line 22; init/review at 70/79; skills folded
in at ~132).

### `GET /skill?directory=` (legacy, PRIMARY for UnoVibe)
Query: `directory` (optional). Response: **bare array** of `Skill.Info`:
```
[ { name: string, description?: string, location: string, content: string } ]
```
Confirmed on the 1.17.18 dev server: `['customize-opencode', 'quickmarkup']`. The equivalent HttpApi
surface is `GET /api/skill?location[directory]=` (wrapped `{location, data}`); on 1.17.x it returns
only the built-in skill.
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

## Implementation plan — **Phase 2 shipped** (all steps landed and building)

1. **`UnoVibe/Models/ServerSuggestionItem.cs`** (new) — `ServerCommandItem {Name, Description, Source, Subtask, Hints[]}`,
   `ServerSkillItem {Name, Description}`, `FileSystemEntry {Path, Type}`.
2. **`UnoVibe/Services/OpencodeClient.cs`** — added, following the existing `JsonDocument` +
   `GetStringProperty` pattern:
   - `GetCommandsAsync(string? directory, CancellationToken)` → legacy `GET /command?directory=`
     first, falling back to `GET /api/command?location[directory]=`. Parses bare arrays OR the
     `{location, data}` wrapper (shared `FetchItemArrayAsync` helper).
   - `GetSkillsAsync(string? directory, CancellationToken)` → legacy `GET /skill?directory=` first,
     falling back to `GET /api/skill?location[directory]=`.
   - `FindFilesAsync(query, directory, type, limit, ct)` → `GET /api/fs/find` only (the legacy
     `/fs/find` route 404s on 1.17.x — verified returns the SPA fallback HTML).
   - `LocationUrl(path, directory)` private helper → `path?location%5Bdirectory%5D=<escaped>`
     (deep-object form, brackets percent-encoded) — used only for the `/api/*` fallbacks.
   - `DirectoryUrl(path, directory)` private helper → `path?directory=<escaped>` — used for the
     legacy routes.
   - `FetchItemArrayAsync` returns the item array (bare array or `data` key) or null when the
     response isn't a usable JSON list, so the primary/fallback chain degrades cleanly.
   - Every method is guarded with try/catch (`HttpRequestException`, `TaskCanceledException`,
     `JsonException`) returning an empty list so the box degrades gracefully.
 3. **`UnoVibe/Services/SuggestionProviders.cs`** — server providers added, **mock providers deleted**
    (no fallback — an unreachable/empty server shows no suggestions, per user request):
    - `ServerCommandSuggestionProvider(Func<OpencodeClient?> client, Func<string> directory)` —
      maps `cmd:` keys (`Kind = command`), MCP entries get a ` :mcp` suffix on `Text` **only as a
      display label** (matches the TUI's `commands` memo in `autocomplete.tsx`; Insert stays clean
      `/name `), `source == "skill"` entries map to `skill:` keys / `Kind = skill`. Commands and MCP
      entries set `InputStartOnly = true` so they only appear when `/` is the first character
      (TUI parity); skills set `InputStartOnly = false` and stay insertable anywhere.
    - `ServerSkillSuggestionProvider` — maps `/skill` (legacy) / `/api/skill` entries to `skill:` keys.
    - `ServerFileSuggestionProvider` (`Trigger = '@'`) — passes the raw query through to
      `FindFilesAsync` (server pre-filters/pre-ranks; **no client re-sort**), `Key = "file:"+path`,
      `Insert = "@"+path+" "`, directories insert a trailing slash (`"@"+path+"/"`).
    - **No fallback**: when `client` is null, the server call throws, or the server returns an empty
      list, all three providers return an empty list — `SuggestBox.ShowSuggestions` closes the flyout
      for empty results. `SuggestionFilter` holds the shared case-insensitive substring filter.
4. **`SuggestionBoxController`** — unchanged: already routes by trigger and passes the raw query
   through; server providers just implement `ISuggestionProvider` with `Trigger = '@'`.
5. **`ChatPage.cs`** — `Ctor` now wires the three server providers (each takes
   `() => Store.Client` + `Store.ActiveDirectory` method group, so the directory tracks the active
   session). No markup changes. `CommitSuggestion` inserting `/name ` + a normal `SendAsync` is all
   that's needed — the server expands the command server-side.
6. **`ChatStore.cs`** — `public OpencodeClient? Client => _client;` accessor added, and
   `ActiveDirectory()` made public (was private).
7. **QA checklist** (manual, per AGENTS.md — do not launch/test the app yourself):
   - [ ] `/` at position 0 lists init, review, user commands (`test-add`); MCP entries carry ` :mcp`
     (display only); skills appear (`/quickmarkup`, `/customize-opencode`) from BOTH the command and
     skill providers (deduped); typing filters; Enter/Tab inserts `/name ` (clean, no `:mcp`); typing
     a space closes the box.
   - [ ] `/` mid-sentence (e.g. `please help with /review-code`) lists only skills — commands/MCP are
     `InputStartOnly` and drop out (TUI parity).
   - [ ] `@` in a session on a non-empty directory lists real files via `fs/find`; Enter inserts `@path `;
     `@foo` (mid-token, no whitespace) does NOT trigger, but `foo @file` does.
   - [ ] Send `/init` and confirm the server executes the init prompt (proves no client-side expansion
     needed).
   - [ ] With server down (or no `OPENCODE_BASE_URL`), the box shows NO suggestions for `/` and `@`
     (no mock fallback).

## Notes / caveats

- `Command.Info.template` is `Schema.Unknown` and can serialize as a Promise stub — never read it from
  the REST list. `hints` (from `$1..$n`/`$ARGUMENTS`) can seed a future "show placeholders" detail.
- **Route skew (why the original Phase 2 missed `/quickmarkup`):** on the running 1.17.18 dev server
  the legacy `/command?directory=` / `/skill?directory=` routes return the COMPLETE data (skills, MCP,
  user commands, `source`), while the newer `/api/*` HttpApi surface returns only built-ins. The
  client therefore prefers the legacy routes and keeps `/api/*` (with its wrapped `{location, data}`
  envelope) as a fallback. If a future server removes the legacy routes, the fallback still works;
  if a future server fixes `/api/*` to fold in skills, either path is fine. `source` missing on
  `/api/*` is treated as `"command"`.
- Skills appear twice in the box on purpose: once from `ServerCommandSuggestionProvider` (folding in
  `source == "skill"` from `/command`) and once from `ServerSkillSuggestionProvider` (`/skill`).
  `SuggestionBoxController.GetSuggestionsAsync` dedupes by `Key` (`skill:<name>` in both), so only one
  row shows.
- Location `workspace` is only needed for workspace-v2 setups; UnoVibe uses plain directories, so
  omit it unless a session reports one.
- The old plain `/fs/list`, `/fs/find` routes found in earlier research **do not exist** — they 404
  (SPA fallback HTML) on 1.17.x, so files use `/api/fs/find` with no legacy fallback.
- Phase 2 is done and building (see the Implementation plan section). AGENTS.md was updated to reflect
  that the suggestion box now uses live server data.
