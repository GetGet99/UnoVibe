# ConnectPage and the connect flow

Reference for `ConnectPage`, the recent-connections list, and the folder/server connect flows.
**Read this file when** editing `ConnectPage`, `ConnectPanel`, `RecentListPanel`,
`RecentConnectionsStore`, `StartupArgs`, `ServeProcess`, or the password/security handling.

The ConnectPage (redesigned VSCode-style) has a two-column layout:
- A **Recent** list (left, fixed-height scroll area so the panel stays consistent whether empty
  or not) of previously opened folders and server URLs.
- Two primary buttons (right) — **Open Folder** and **Connect to URL**.

The whole content block is centered horizontally and vertically (a code-behind
`ViewChanged`/`SizeChanged` handler on the ScrollViewer keeps the inner StackPanel
`MinHeight` = viewport height so it stays centered while still scrolling when the window is small).

**Small-screen layout** (responsive, in `ConnectPage.cs`):
the page tracks a `IsCompact` reference (updated in `OnScrollHostSizeChanged` when the
ScrollViewer viewport width crosses `CompactBreakpoint = 820`). The recent/connect panels live in
a single Grid whose `ColumnDefinition.Width`/`RowDefinition.Height`/`RowSpacing` and the connect
panel's attached `Grid.Row`/`Grid.Column` are reactive on `IsCompact`: wide → the original
side-by-side `1.4*`/`*` two-column grid; compact → both panels stack full-width (col 1 collapses
to 0px, connect panel moves to row 1, `RowSpacing=16`). The content `Padding` also shrinks in
compact mode. No panel is remounted when the layout switches (only grid placement changes), and
the horizontal status row + ConnectPanel's save/forget row use `WrapPanel` so long text wraps
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
Folders launched via `opencode serve` generate a cryptographically-random 32-char password by default
(so only this app can connect), or accept a custom password + confirmation; custom passwords are
validated (set + match) in `StartServeCoreAsync`.

**Raw custom password persistence:**
The raw custom password is NOT persisted by default — saving it is opt-in via a small **Save/Forget**
button (next to the password boxes) that opens a confirmation flyout warning it will be stored in
plain text on the device (`SetSavePassword`); the `savePassword` flag in `recent.json` gates it and
`SaveSecurity(useGenerated, savePassword, customPassword)` only writes `customPassword` when opted in.

The spawned server is owned by `ChatStore.AttachServeProcess(...)` so it survives navigation —
do not re-introduce a `using var serve` that disposes it early.

**Recent history persistence:**
`Services/RecentConnectionsStore.cs` keeps an `ObservableCollection<RecentConnection>` (model in
`Models/RecentConnection.cs`) saved as JSON at
`Windows.Storage.ApplicationData.Current.LocalFolder.Path/recent.json` (Skia desktop resolves this
to `~/.local/share/UnoVibe/<AppId>/LocalState/` on Linux — e.g.
`/home/get/.local/share/UnoVibe/com.companyname.unovibe/LocalState/`).
The file is an object `{ useGeneratedPassword, savePassword, customPassword, items[] }`; legacy
bare-array files are migrated on load.
Upserts happen only on a successful connect (after `ConnectionStatus == "Connected"`); the list is
capped at 20 entries and keyed by normalized path/URL.
Server entries persist a `RequiresPassword` flag instead of the password itself
(`UpsertServer(url, requiresPassword)`); legacy entries that stored a raw `serverPassword` are
migrated on load to `RequiresPassword=true` so reopening prompts for the password
(`CollectLegacyPasswordKeys` scans the raw JSON).
Clicking a flagged server entry opens a `ContentDialog` password prompt
(`ConnectPage.PromptForServerPasswordAsync`) — the entered password is used for that connection only
and never written back.

The markup `foreach` over `RecentConnectionsStore.Items` is keyed by `item.Key` and uses
`Items.Reactive.Count` for the empty-state/`Clear all` visibility.
Note: QuickMarkup can't parse XAML-style `1.4*` star widths in `ColumnDefinition.Width` —
use a backtick `new GridLength(1.4, GridUnitType.Star)` instead.