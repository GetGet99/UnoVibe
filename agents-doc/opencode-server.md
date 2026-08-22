# opencode server integration

Reference for how UnoVibe talks to `opencode serve` and reacts to its events.
**Read this file when** working on `OpencodeClient`, `ChatStore.Apply`, `ServeProcess`,
permission/question handling, MCP, or anything that touches the server API or the SSE event stream.
Client-side session state (send modes, revert, fork, retry/continue, autoscroll) lives in
[`session-state.md`](session-state.md); sidebar rendering lives in [`session-sidebar.md`](session-sidebar.md).

## Auth

Basic auth `Authorization: Basic base64(username:password)`.
Env vars: `OPENCODE_SERVER_PASSWORD`, `OPENCODE_SERVER_USERNAME` (default username `opencode`).
Password empty/unset ⇒ unsecured.
**Every** endpoint requires auth when a password is set — including `GET /global/health` —
so health/startup probes must send the header too.
Auth source: `packages/opencode/src/server/auth.ts`.

## Startup readiness

Poll `GET /global/health` until it returns `{"healthy":true,...}`.

## SSE events

`GET /event` (long-lived stream; **scoped to the request's instance directory** —
events for sessions in other directories are filtered out server-side, so
`ChatStore.StartFolderEventStream` opens an extra `/event?directory=<path>` stream per opened sidebar
folder, feeding the same channel; `PumpAsync` dedupes by SSE event id because a folder equal to the
server's default instance would otherwise deliver every event twice).

**Worktree caveat:** git worktrees of the same repo share one project ID, so the default `GET /session`
list can include sessions from *other* worktree directories (their events are tagged with that directory
and delivered **only** on a directory-scoped stream).
`RefreshSessionsAsync` therefore opens a stream for **every directory that contributes sessions to the
sidebar**, not just explicitly-opened folders (`_openedFolders`) — without it, a worktree session would
appear in the sidebar but send messages "into the void" (turn runs server-side, no event ever reaches
the app).

## Session API

- `POST /session` — create; omit `title` so the server assigns a default and auto-generates a name
  (see "[Titles](#titles)").
- `GET /session` — list; **scoped by project + directory** — the server's `Session.list` filters by
  the instance's project ID, so sessions created in *other directories* of a different project
  (via `POST /session?directory=`) are NOT in the default list, which is why
  `ChatStore.RefreshSessionsAsync` additionally fetches `GET /session?directory=<path>` per opened
  sidebar folder and merges the results; but worktree directories of the same repo share the project
  ID and DO show up in the default list — see "[SSE events](#sse-events)" for why each such directory
  still needs its own event stream.
- `PATCH /session/:id` with `{ title }` — rename; this is how the TUI renames and how the server's
  title generator writes names.
- `POST /session/:id/abort` — interrupt the running turn.
- `POST /session/:id/command` — invoke a custom command (see "Slash-command send" in
  [`suggest-box.md`](suggest-box.md)). Runs the whole command turn server-side and blocks
  until it completes.

## Titles

`POST /session` with no title yields a default `"New session - <ISO>"`/`"Child session - <ISO>"`.
On the first prompt the server runs a `title` agent with the small model (`provider.getSmallModel`)
and replaces the default via `session.setTitle` (source: `session/prompt.ts` `SessionPrompt.ensureTitle`;
regex in `session/session.ts` `isDefaultTitle`).
The write emits a `session.updated` event carrying `{ sessionID, info }`, which
`ChatStore.ApplySessionUpsert` applies to the sidebar + header.
UnoVibe creates sessions without a title, displays `"New Chat"` for default-titled sessions
(`NormalizeTitle`), and surfaces the generated name when the event arrives.
Manual rename (`ChatStore.RenameSessionAsync`, header ✎ button) calls `PATCH /session/:id` and
short-circuits future auto-naming because the title no longer matches `isDefaultTitle`.

## Subagents

The `task` tool spawns a child session whose `SessionInfo` carries a `parentID`
(field `SessionInfo.ParentId`; `IsSubagent` = `ParentId` non-empty).
`ChatStore` keeps subagent `SessionInfo`s in `Sessions` (needed for `SwitchSessionAsync` lookup +
unread tracking) but **filters them out of `ReconcileDirectoryGroups`**, so they never appear in the
sidebar — mirroring the TUI (`parentID === undefined` filter).

Entry point is the tool call itself:
`ApplyToolState` parses the `task` part's `state.metadata.sessionId`/`parentSessionId` and
`state.input.subagent_type` into `PartItem.ToolSessionId`/`ToolParentSessionId`/`ToolSubagentType`,
and `MessageView` dispatches `tool == "task"` to `ToolViewTask` — a clickable card
(✳ title + subagent-type pill + status line + ✓/✕/■) that calls
`ChatStore.SwitchSessionAsync(part.ToolSessionId)` on click.

Opening a subagent session shows a **back button before the title** in the ChatPage header
(`Store.ParentSessionId.Length > 0`) that calls `ChatStore.GoToParentAsync()`; `ParentSessionId` is
set in `SwitchSessionAsync` (with a `GET /session/:id` fallback via `OpencodeClient.GetSessionAsync`
when the child isn't in the sidebar list) and reset in
`Configure`/`NewSessionAsync`/`EnsureSessionAsync`/`ApplySessionDeleted`.

## Permission API

- `GET /permission` — list pending.
- `POST /permission/:requestID/reply` with `{ reply: "once"|"always"|"reject", message? }`.
- Events: `permission.asked` (properties = the full `PermissionV1.Request`:
  `{ id, sessionID, permission, patterns[], metadata{}, always[], tool?: {messageID, callID} }`)
  and `permission.replied` (`{ sessionID, requestID, reply }`).

**Pending permission requests are per workspace directory (instance).**
`OpencodeClient.GetPendingPermissionsAsync`/`ReplyPermissionAsync` take a `directory` and are called
with the active session's instance (`ChatStore.SyncPendingPermissionsAsync` uses `ActiveDirectory()`;
`ReplyPermissionAsync` resolves the request's session directory via `PermissionDirectory`), so replies
reach the instance that owns the request (folder-opened sessions live in a non-default instance —
a directory-less reply would 404).

`ChatStore` keeps a pending-request queue (`ActivePermission` = oldest pending) that is
**rebuilt from the authoritative server list** on connect/session-switch
(`SyncPendingPermissionsAsync` clears `_permissions` then re-adds requests for the active session,
deduped by `AddPermissionRequest`) — the server is the source of truth because a request can vanish
with **no `permission.replied` event** when its turn is aborted/interrupted or its instance is disposed
(`Effect.ensuring`/`InstanceState` finalizers in `permission/index.ts` just delete/fail the pending
entry). A reply that comes back 404 (`HttpRequestException` with `StatusCode == NotFound`) drops the
stale request from the queue so the next pending one surfaces instead of a dead card.

`permission.asked/replied` are NOT session-filtered in `Apply` (subagents run in their own sessions) —
the per-session `SessionFlags.PendingPermissions` counter still drives the sidebar attention indicator.
The active-view queue (`AddPermissionRequest`) accepts a request when its session is the active session
**or a descendant of it** (`IsActiveOrDescendant` walks the `SessionInfo.ParentId` chain), so a task
child's pending permission surfaces in the parent's dialog and can be approved without navigating into
the subagent.

The UI shows an inline allow/always/reject dialog above the input and disables sending while one
is pending.

## Status / errors

`session.status` events carry
`{ sessionID, status: {type:"idle"|"busy"|"retry", attempt?, message?, action?, next?} }`;
the TUI treats anything `!= "idle"` as busy and shows the retry message.
`ChatStore.StatusMessage` surfaces the retry banner.

`session.status`, `message.updated`, and the `question.*` events are intentionally **not**
session-filtered in `Apply` — `ChatStore` tracks per-session busy state (`SessionFlags.Status` →
`SessionInfo.IsBusy`) to drive the sidebar spinner, and polls `GET /session/status` at connect to
catch sessions already busy before the SSE stream attached (the server only emits status on
transitions).

Unread (`SessionInfo.IsUnread`, a client-side concept — the server has no read/unread tracking) is
set when a *background* session's turn completes (`session.status` → idle while not active). The
value is deliberately **kept** when the session is opened — viewing sets the separate `IsRead` flag
(`SessionInfo.IsRead` / `SessionFlags.Read`), which merely *suppresses* the indicator, so the sidebar
context menu can re-show it without losing state. **Right-clicking a sidebar session** opens a
`ContextFlyout` `MenuFlyout` with **Mark as unread / Mark as read**
(`ChatStore.SetSessionRead`, which also asserts `IsUnread` on mark-as-unread so the dot shows even
for a session with no finished turn). A new background completion clears `Read` again so the
indicator reappears. The turn outcome (`SessionInfo.Outcome`:
`success`/`error`/`interrupted`) is derived client-side from the background session's last assistant
`message.updated` `info.error` (`MessageAbortedError` → interrupted, any other error → error,
none → success; mirrors the web client's `rows.ts` logic) and drives the sidebar icon (✓/✕/■)
+ color.

Pending attention (`SessionInfo.NeedsAttention`/`AttentionKind`) is tracked per-session from
`permission.asked/replied` and `question.asked/replied/rejected` counts
(`SessionFlags.PendingPermissions`/`PendingQuestions`, seeded at connect + switch via
`SyncPending*Async` from `GET /permission` + `GET /question`) and shows a `Permissions`/`Help` glyph
in `SystemAttention` that **overrides** the busy spinner (mirrors the web client's `needsAttention`).

**Inline question form** (`ToolViewQuestion`/`ToolViewQuestionItem`):
- Submits via `POST /question/:requestID/reply`
  (`{ answers: [[label,...], ...] }`, one array per question — `"Unanswered"` if empty).
- Dismisses via `POST /question/:requestID/reject` (no body), which fails the question tool with
  `QuestionRejectedError` so the agent sees it was declined.
- Per question, a `custom` field adds a "Type your own answer..." option that is exclusive with
  the options (single) or combinable (multi) and enables the text box only while selected.
- **Pending questions are per workspace directory (instance), like permissions** — the server's
  pending map lives in `InstanceState` (`question/index.ts`), so `ReplyQuestionAsync`/
  `RejectQuestionAsync`/`GetPendingQuestionsAsync` take a `directory` and are called with the
  owning session's instance (`ChatStore.QuestionDirectory`/`DirectoryOf`, `SyncPendingQuestionsAsync`
  uses `ActiveDirectory()`), so a reply reaches the instance holding the request
  (folder-opened sessions live in a non-default instance — a directory-less reply 404s
  `QuestionNotFoundError`). A 404 reply/reject drops the stale request so the next pending
  question surfaces instead of a dead form.

**Assistant message errors** (`info.error`) are rendered as an `error` part box
(`UnknownError` e.g. `"Streaming response failed: [503]..."`);
`MessageAbortedError` maps to the interrupted part instead.
Error message strings may contain surrounding literal quotes — `UnwrapErrorMessage` strips them.

**Auto-retry card:**
The active turn's auto-retry (`status type "retry"` with `attempt`/`message`/`next` unix-ms) drives
an **end-of-chat retry card** (`ChatStore.IsRetrying`/`RetryMessage`/`RetryAttempt`/`RetryNextMs`;
`ChatPage` ticks a `DispatcherTimer` every second calling `UpdateRetryCountdown` for the live
"retrying in Ns · attempt #N" line — the header `StatusMessage` banner also still shows it).

**Continue button:**
A stopped-with-error turn shows a **"⟳ Continue" button** (`SessionStore.ShowContinue`), set at
`session.status` idle or when the final `message.updated` lands after idle (the server emits idle
before the error-carrying `message.updated`, since `halt` runs before `cleanup`), and computed by
`ShouldShowContinue()` = `LastAssistantMessageErrored()` (last assistant message has an `error` part)
**or** `LastAssistantMessageEndsOnThinking()` (the chat visibly ends on a Thinking/reasoning part —
a turn that stops mid-reasoning or finishes reasoning-only often carries no `error` part to latch
onto); aborts never qualify (interrupt → "aborted" part).
The button (a bare left-aligned button — no card, since the error part box above already surfaces
the error; tooltip explains it sends a `"continue"` message) just calls `Store.SendAsync("continue")`
— there is **no server continue API**; the agent prompt (`prompt/beast.txt`) tells the model to
resume from the last incomplete todo step (matches the TUI, which only lets the user type it).
Flags reset in `ResetTurnFlags()` on connect/new/switch/delete and before each send.

## MCP API

- `GET /mcp` → `Record<name, {status, error?}>` where status ∈
  `connected|disabled|failed|needs_auth|needs_client_registration`.
- `POST /mcp/:name/connect` / `POST /mcp/:name/disconnect` (disconnect ⇒ status `disabled`).
- `POST /mcp` (add).
- OAuth routes `/mcp/:name/auth` (+ `/auth/callback`, `/auth/authenticate`,
  `DELETE /mcp/:name/auth`).

**MCP status is per workspace directory (instance), NOT per session** — all sessions in a directory
share the same MCP servers from that directory's `opencode.json` `mcp` key; the TUI routes via the
`?directory=`/`x-opencode-directory` instance header.
There is **no push event for MCP status changes** (only `mcp.tools.changed` /
`mcp.browser.open.failed`), so clients poll `/mcp` at connect, on session switch, and after each
toggle — exactly what `ChatStore.RefreshMcpStatusAsync` does.

UnoVibe shows a collapsible **MCP section in `SessionSidebar`** (status dot + name + status/error +
Connect/Disconnect toggle, summary `N active, M error`); `ChatStore.ToggleMcpAsync` calls
connect/disconnect/authenticate based on current status (mirrors the web client's `toggleMcp`):
connected → disconnect, `needs_auth` → **`POST /mcp/{name}/auth/authenticate`**, anything else →
connect. A `needs_auth` toggle therefore runs the server-side OAuth flow: the **server** opens the
default browser on the authorization URL and the request **blocks** until the redirect returns to
its own local callback server (up to 5 minutes), storing the tokens; the button label reads
"Authenticate" (and "Authenticating…" while in flight). `OpencodeClient.McpAuthenticateAsync` uses a
dedicated `HttpClient` with a 6-minute timeout because the shared client's 100s default would abort
the wait, and surfaces the returned status (`{status, error}`) as the new `McpServerInfo`.

The remaining OAuth routes (`POST /mcp/{name}/auth` start, `POST .../auth/callback` `{code}`,
`DELETE /mcp/{name}/auth` remove) exist but are unused — the blocking `authenticate` route covers
the browser-based flow UnoVibe needs.
TUI source: `packages/tui/src/feature-plugins/sidebar/mcp.tsx` + `context/local.tsx`;
API def: `server/routes/instance/httpapi/groups/mcp.ts`.

## Unhandled events

`ChatStore.Apply` has `// TODO:` placeholder `case`s (with `break;`) for every other event the
server's `/event` stream emits:
`session.deleted/error/diff/idle/compacted`, `file.edited`, `file.watcher.updated`,
`todo.updated`, `lsp.updated`, `command.executed`,
`mcp.browser.open.failed`, `server.connected/heartbeat/instance.disposed`, `tui.toast.show`.

Handled: `session.created`/`session.updated`, `session.status`, `message.removed`,
`question.replied`/`question.rejected` (pending-attention counters),
`mcp.tools.changed` (→ `RefreshMcpStatusAsync`),
and `vcs.branch.updated` (→ `ChatStore.RefreshBranches`).

The `session.next.*` streaming events exist in the schema but are not published by the current CLI
server. Implement a case and remove its TODO marker when adopting it.

## Serve flags & port probing

- `opencode serve` flags: `--port` default 0 (random), `--hostname` default `127.0.0.1`.
  Server instance is resolved per-request via the `x-opencode-directory` header,
  so it can be launched from any directory.
- Port probing at runtime should use a real bind (e.g., `TcpListener` on `127.0.0.1:0`,
  or Python `socket`); bash `shuf` can pick an occupied port.