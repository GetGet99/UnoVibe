# opencode server integration

Reference for how UnoVibe talks to `opencode serve` and reacts to its events.
**Read this file when** working on server API, SSE event handling, permissions/questions, MCP,
or serve processes. Client-side session state lives in [`session-state.md`](session-state.md);
the sidebar model is in [`session-sidebar.md`](session-sidebar.md).

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

`GET /event` is long-lived and **scoped to the request's instance directory** — events for
sessions in other directories are filtered out server-side, so the client opens an extra
directory-scoped stream per opened sidebar folder. Batch dispatch is per-event guarded: one
failing handler no longer drops the rest of the batch.

**Worktree caveat:** git worktrees of the same repo share one project ID, so the default
`GET /session` list can include sessions from *other* worktree directories (their events are
tagged with that directory and delivered **only** on a directory-scoped stream). The client
therefore opens a stream for **every directory that contributes sessions to the sidebar** —
without it, a worktree session would appear in the sidebar but send messages "into the void".

## Session API

- `POST /session` — create; omit `title` so the server assigns a default and auto-generates a
  name (see "Titles").
- `GET /session` — list; **scoped by project + directory**: sessions created in *other
  directories* of a different project are NOT in the default list, which is why the client
  additionally fetches per opened sidebar folder and merges the results. Worktree directories of
  the same repo share the project ID and DO show up in the default list — but each such
  directory still needs its own event stream (see "SSE events").
- `PATCH /session/:id` with `{ title }` — rename; this is how the TUI renames and how the
  server's title generator writes names.
- `POST /session/:id/abort` — interrupt the running turn.
- `POST /session/:id/command` — invoke a custom command (see "Slash-command send" in
  [`suggest-box.md`](suggest-box.md)). Runs the whole command turn server-side and blocks until
  it completes.

## Titles

`POST /session` with no title yields a server default. On the first prompt the server runs a
`title` agent with the small model and replaces the default via a `session.updated` event.
UnoVibe creates sessions without a title, displays `"New Chat"` for default-titled sessions, and
surfaces the generated name when the event arrives. Manual rename short-circuits future
auto-naming because the title no longer matches the server's default-title pattern.

## Subagents

The `task` tool spawns a child session whose info carries a `parentID`. Subagent sessions are
kept for lookup but **filtered out of the sidebar**, mirroring the TUI. The tool call links to a
clickable card that switches to the child session. Opening a subagent shows a **back button**
returning to the parent (with a `GET /session/:id` fallback when the child isn't listed).

## Permission API

- `GET /permission` — list pending.
- `POST /permission/:requestID/reply` with `{ reply: "once"|"always"|"reject", message? }`.
- Events: `permission.asked` (the full request) and `permission.replied`.

**Pending permission requests are per workspace directory (instance).** Client methods take a
`directory` and are called with the owning session's instance, so replies reach the instance
that owns the request (a directory-less reply would 404).

The pending-request queue is **rebuilt from the authoritative server list** on connect/session-switch —
the server is the source of truth because a request can vanish with **no `permission.replied`
event** when its turn is aborted or its instance is disposed. A reply that comes back 404 drops
the stale request so the next pending one surfaces instead of a dead card.

`permission.asked/replied` are NOT session-filtered (subagents run in their own sessions).
A child's pending permission surfaces in the parent's dialog when its session is the active
session **or a descendant of it**, so it can be approved without navigating into the subagent.
The UI disables sending while one is pending.

## Status / errors

`session.status` events carry `{ sessionID, status: {type:"idle"|"busy"|"retry", ...} }`;
the TUI treats anything `!= "idle"` as busy and shows the retry message.

`session.status`, `message.updated`, and the `question.*` events are intentionally **not**
session-filtered — per-session busy state drives the sidebar spinner, and the client polls
`GET /session/status` at connect to catch sessions already busy before the SSE stream attached
(the server only emits status on transitions).

Background session activity: when a *background* session's turn completes while not active, the
sidebar marks it unread with the turn outcome derived from the last assistant `message.updated`
error; viewing the session marks it read. Pending-permission/question attention **overrides** the
busy spinner (mirrors the web client).

**Inline question form:**
- Submits via `POST /question/:requestID/reply`
  (`{ answers: [[label,...], ...] }`, one array per question — `"Unanswered"` if empty).
- Dismisses via `POST /question/:requestID/reject` (no body), which fails the question tool so
  the agent sees it was declined.
- A `custom` field adds a "Type your own answer..." option.
- **Pending questions are per workspace directory (instance), like permissions** — reply/reject
  take a `directory` (a directory-less reply 404s). A 404 reply/reject drops the stale request.

**Assistant message errors** render as an `error` part box; aborts map to the interrupted part
instead. Surrounding literal quotes in error strings are stripped before display.

**Auto-retry card:** the active turn's auto-retry drives an end-of-chat retry card with a live
"retrying in Ns · attempt #N" line (the header banner shows it too).

**Continue button:** a stopped-with-error turn shows a **Continue button** when the last assistant
message carries an `error` part **or** the chat visibly ends on a Thinking/reasoning part; aborts
never qualify. The button just sends the literal `"continue"` message — there is **no server
continue API** (matches the TUI, which only lets the user type it).

## MCP API

- `GET /mcp` → `Record<name, {status, error?}>` where status ∈
  `connected|disabled|failed|needs_auth|needs_client_registration`.
- `POST /mcp/:name/connect` / `POST /mcp/:name/disconnect` (disconnect ⇒ status `disabled`).
- OAuth routes under `/mcp/:name/auth` — only the blocking `authenticate` route is used.

**MCP status is per workspace directory (instance), NOT per session** — all sessions in a
directory share the same MCP servers from that directory's `opencode.json` `mcp` key.
There is **no push event for MCP status changes** (only `mcp.tools.changed` /
`mcp.browser.open.failed`), so clients poll `/mcp` at connect, on session switch, and after each
toggle. UnoVibe shows a collapsible **MCP section in the sidebar** mirroring the web client's
toggle mapping; the `needs_auth` flow uses a dedicated long-timeout client because the shared
client's default would abort the wait.

## Unhandled events

The event applier keeps placeholder cases for every other event the server's `/event` stream
emits (`session.deleted/error/diff/idle/compacted`, `file.edited`, `file.watcher.updated`,
`todo.updated`, `lsp.updated`, `command.executed`, `mcp.browser.open.failed`,
`server.connected/heartbeat/instance.disposed`, `tui.toast.show`).

Handled: `session.created`/`session.updated`, `session.status`, `message.removed`,
`question.replied`/`question.rejected` (pending-attention counters),
`mcp.tools.changed` (→ MCP status refresh), and `vcs.branch.updated` (→ branch refresh).

The `session.next.*` streaming events exist in the schema but are not published by the current
CLI server. Implement a case when adopting it.

## Serve flags & port probing

- `opencode serve` flags: `--port` default 0 (random), `--hostname` default `127.0.0.1`.
  Server instance is resolved per-request via the `x-opencode-directory` header,
  so it can be launched from any directory.
- Port probing at runtime should use a real bind (e.g., `TcpListener` on `127.0.0.1:0`);
  bash `shuf` can pick an occupied port.
