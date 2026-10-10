# Session sidebar

Reference for `SessionSidebar` and the sidebar model.
**Read this file when** editing `SessionSidebar` or the session/busy/outcome indicators.
Session flags (busy/outcome/state) are *derived* from server events — see the
"Status / errors" section of [`opencode-server.md`](opencode-server.md); the MCP section's
server API + toggle mapping is also there.

## Sidebar ordering (`SortKey`)

Sessions sort by a client-side `SortKey`, **not** by the server's `time.updated` — the server
bumps `updated` on background activity (compaction summaries, permission/revert touches,
other clients' prompts), which used to flip the order every few seconds while sessions ran.

- `SortKey` is seeded from `time.updated` when the head is created, so initial order is
  most-recent-first as before.
- It is bumped to "now" only on local user sends. Silent auto-continue loops never bump it.
- Server updates keep syncing the relative time label but leave `SortKey` alone, so the label
  still reflects last server activity while the order reflects your last touch.

## Directory groups

Because the server's plain `GET /session` list is scoped to its default project/instance,
the client also fetches per opened folder and merges those sessions in (deduped by id).
Opening a folder also starts a directory-scoped `/event` stream so a session created in it
updates live. Folders with no sessions still show an empty group instead of vanishing.

Each sidebar directory group shows its git branch after the folder name.

## Connection details

The sidebar bottom status border has a **Connection details** button opening a flyout with the
current connection's **directory**, **URL** and **password**, each as a selectable line plus
a copy button. The password is **masked by default** (or "None" when the server has no password).
The values are the effective connection values (password resolved with env-var fallback).
