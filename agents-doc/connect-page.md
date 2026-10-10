# ConnectPage and the connect flow

Reference for `ConnectPage`, the recent-connections list, and the folder/server connect flows.
**Read this file when** editing the connect flow or the password/security handling.
(The `IsCompact`/`IsSidebarView` system used by MainPage/ChatPage is separate — see
[`responsive-layout.md`](responsive-layout.md).)

## Product decisions

- **Open Folder is one click:** picking a folder immediately launches `opencode serve` there
  and connects — there is no separate "Start & connect" step. The spawned server is owned by
  the connection so it survives navigation — do not dispose it early.
- **Folder security toggle/password is the single source of truth for folder passwords**
  (used for both recent folders and new ones), persisted globally. Folders launched via
  `opencode serve` generate a cryptographically-random password by default (so only this app
  can connect), or accept a custom password + confirmation. The raw custom password is NOT
  persisted by default — saving it is opt-in via a Save/Forget button with a plain-text
  warning.
- **Server URLs never persist their password** — only a `RequiresPassword` flag (a server
  connected with a password is flagged so reopening prompts for it). The entered password is
  used for that connection only and never written back.
- Recent history is kept in an observable collection saved as JSON in the app's local-data
  directory. Legacy file shapes are migrated on load. Upserts happen only on a successful
  connect; the list is capped and keyed by normalized path/URL.
