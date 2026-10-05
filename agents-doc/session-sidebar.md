# Session sidebar

Reference for `SessionSidebar` and the sidebar state kept in `SessionsStateProvider`.
**Read this file when** editing `SessionSidebar`, `ReconcileDirectoryGroups`, folder actions,
the connection-details flyout, or the session/busy/outcome indicators.
Session flags (busy/outcome/state) are *derived* from server events — see the
"Status / errors" section of [`opencode-server.md`](opencode-server.md); the MCP section's
server API + toggle mapping is also there.

> **No-rebuild sidebar model:** sidebar state lives on persistent instances — group and
> session items are reused (never recreated) and reconciled in place (drop gone / update
> survivors / append new, reordering with `Move`). Per-session sidebar flags stay the
> authoritative store for busy/outcome/pending-attention because SSE can fire for sessions
> not yet in the list.

## Git branch in the sidebar

Each sidebar directory group shows its git branch (`⎇ <branch>`) after the folder name, from
`GET /vcs?directory=<path>`. Group instances are **reused** across reconciles, so the reactive
`Branch`/expansion fields live on the object and survive refreshes. Branches re-fetch in place
after session refreshes and on the `vcs.branch.updated` SSE event.

## Sidebar folder actions

Each directory-group header shows two small icon buttons left of the "+" (new session)
button — editor and file manager. They delegate to the folder-launcher helper, which validates
the directory then launches `<command> <dir>` where the command is the **Default IDE/Editor**
setting (default `code` — see [`settings.md`](settings.md)) and, for the file manager, the OS
default. Launch failures surface as an error toast.

**Open Folder button:**
the sidebar's **Open Folder** button (bottom status border, next to the connection-status row)
starts a new unsaved session in the picked folder. It is a small icon button — not the top.

Folders opened with it — or with a group's "+" button — are tracked as opened folders and
**shown in the sidebar even when the server returns no sessions for them** (an empty group with
a muted "No sessions yet" line instead of a session list).
Because the server's plain `GET /session` list is scoped to its default project/instance,
the client also fetches `GET /session?directory=<path>` per opened folder and merges those
sessions in (deduped by id). Opening a folder also starts a directory-scoped `/event` stream so
a session created in it updates live.

## Connection details

The sidebar bottom status border has a **Connection details** icon button (vertical-ellipsis
glyph, right of the "New window" button).
It opens a flyout showing the current connection's **directory**, **URL** and **password**, each as
a selectable line plus a copy button.
The password is **masked by default** (or "None" when the server has no password) with an eye
toggle to reveal it; the eye + copy buttons are hidden entirely when the server has no password.
The values are the effective connection values (password resolved with env-var fallback).