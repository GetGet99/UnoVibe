# ConnectPage and the connect flow

Reference for `ConnectPage`, the recent-connections list, and the folder/server connect flows.
**Read this file when** editing `ConnectPage`, `ConnectPanel`, `RecentListPanel`,
`RecentConnectionsStore`, `StartupArgs`, `ServeProcess`, or the password/security handling.

The ConnectPage has a two-column layout:
- A **Recent** list (left, fixed-height scroll area so the panel stays consistent whether empty
  or not) of previously opened folders and server URLs.
- Two primary buttons (right) — **Open Folder** and **Connect to URL**.

The whole content block is centered horizontally and vertically while still scrolling when the
window is small.

**Small-screen layout:**
below a compact width breakpoint the two panels stack full-width instead of side-by-side.
No panel is remounted when the layout switches (only grid placement changes), and text rows wrap
instead of overflowing on narrow windows.
(The `IsCompact`/`IsSidebarView` system used by MainPage/ChatPage is separate — see
[`responsive-layout.md`](responsive-layout.md).)

**Open Folder is one click:**
picking a folder immediately launches `opencode serve` there and connects — there is no separate
"Start & connect" step.

**Folder security toggle/password:**
The "Folder security" toggle/password block on the right is the **single source of truth for folder
passwords** (used for both recent folders and new ones via Open Folder), persisted globally
(`SaveSecurity`) and restored in the page ctor; server URLs never persist their password —
`UpsertServer` only records a `RequiresPassword` flag (a server connected with a password is flagged
so reopening prompts for it).

**Folder password generation:**
folders launched via `opencode serve` generate a cryptographically-random password by default
(so only this app can connect), or accept a custom password + confirmation.

**Raw custom password persistence:**
the raw custom password is NOT persisted by default — saving it is opt-in via a small
**Save/Forget** button that opens a confirmation flyout warning it will be stored in plain text
on the device.

The spawned server is owned by the connection so it survives navigation — do not dispose it early.

**Recent history persistence:**
recent connections are kept in an observable collection saved as JSON at
`Windows.Storage.ApplicationData.Current.LocalFolder.Path/recent.json`. Legacy bare-array files
are migrated on load.
Upserts happen only on a successful connect; the list is capped at 20 entries and keyed by
normalized path/URL.
Server entries persist a `RequiresPassword` flag instead of the password itself; legacy entries
that stored a raw password are migrated on load so reopening prompts for the password.
Clicking a flagged server entry opens a password prompt — the entered password is used for that
connection only and never written back.

Note: QuickMarkup can't parse XAML-style `1.4*` star widths — use a backtick
`new GridLength(1.4, GridUnitType.Star)` instead (see
[`quickmarkup.md`](quickmarkup.md)).