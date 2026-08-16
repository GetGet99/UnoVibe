# Session sidebar

Reference for `SessionSidebar` and the sidebar state kept in `ChatStore`.
**Read this file when** editing `SessionSidebar`, `ReconcileDirectoryGroups`, folder actions,
the connection-details flyout, or the session/busy/unread indicators.
Session flags (busy/unread/outcome/attention) are *derived* from server events — see the
"Status / errors" section of [`opencode-server.md`](opencode-server.md); the MCP section's
server API + toggle mapping is also there.

> **No-rebuild sidebar model:** sidebar state lives on persistent instances — `SessionInfo`
> items and `DirectoryGroup` groups are reused (never recreated). `RefreshSessionsCoreAsync`
> reconciles `Sessions` in place (drop gone / update survivors via `ApplySessionUpdate` /
> append new), `ReconcileDirectoryGroups` reconciles each group's `Session` list via
> `ReconcileSessionCollection` (Remove/Insert/Move, reference-identity) and reorders groups
> with `ObservableCollection.Move`, and `ReconcileActiveSubagents` does the same for the chat
> page's subagent strip. Per-session sidebar flags (`_sessionFlags`, keyed by session id) stay
> the authoritative store for busy/unread/outcome/attention because SSE can fire for sessions
> not yet in the list (subagent permission races, background outcome before listing).

## Git branch in the sidebar

Each sidebar directory group shows its git branch (`⎇ <branch>`) after the folder name, from
`GET /vcs?directory=<path>` (`OpencodeClient.GetBranchAsync`, returns `{ branch, default_branch }`).
`ChatStore` keeps a `Dictionary<string, DirectoryGroup> _groupsByDirectory` index; `DirectoryGroup`
instances are **reused** (never recreated) across `ReconcileDirectoryGroups`, so the reactive
`Branch`/`IsExpanded` fields live on the object and survive refreshes with no re-seeding.
`RefreshBranches()` re-fetches every sidebar directory group's branch in place (no rebuild) and is
called after session refreshes and on the `vcs.branch.updated` SSE event.

## Sidebar folder actions

Each `SessionSidebar` directory-group header shows, left of the "+" (new session) button, two small
icon buttons — `Symbol.Code` (editor, tooltip "Open folder in editor") and `Symbol.OpenLocal`
(file manager, tooltip "Open folder in file manager").
`SessionSidebar.RunFolderAction` delegates to `Services/FolderLauncher.cs`
(`OpenInEditor`/`OpenInFileManager`), which validates `Directory.Exists` then launches
`<command> <dir>` where the command is the **Default IDE/Editor** setting (`SettingsStore.EditorCommand`,
default `code` — see [`settings.md`](settings.md)) and, for the file manager, `explorer.exe <dir>`
on Windows or `open`/`xdg-open <dir>` on macOS/Linux. Launch failures surface as an error toast via
`Store.ShowToast`. `Symbol.Code` is defined as `(Symbol)0xe943` in `SymbolExtemsion.cs`.

**Open Folder button:**
The sidebar's **Open Folder** button (a folder picker that starts a new unsaved session in the picked
folder via `ChatStore.NewSessionAsync`) is a small icon button (`Symbol.Folder`, tooltip "Open Folder")
in the bottom status border, sitting next to the "New window" icon button on the right of the
connection-status row — not the top.

Folders opened with it — or with a group's "+" button — are tracked in `ChatStore._openedFolders`
and **shown in the sidebar even when the server returns no sessions for them**:
`ReconcileDirectoryGroups` merges an empty group per opened folder (keyed by normalized path, sorted by
last-opened time, cleared on `Configure`), rendering the group header plus a muted "No sessions yet"
line instead of a session list.
Because the server's plain `GET /session` list is scoped to its default project/instance,
`RefreshSessionsAsync` also fetches `GET /session?directory=<path>` for every opened folder and merges
those sessions in (deduped by id), so a picked folder's existing chats show up too —
`NewSessionAsync` fires that background refresh and calls `ReconcileDirectoryGroups()` immediately so
the folder appears right away (a re-entrancy guard on `RefreshSessionsAsync` coalesces a post-create
refresh racing the background one).
Opening a folder also starts a directory-scoped `/event` stream (`StartFolderEventStream`) so a
session created in it updates live instead of showing an empty chat until a switch-away/back reload.

## Connection details

The sidebar bottom status border has a fourth icon button (`Symbol.More`, the vertical-ellipsis
glyph, tooltip "Connection details") right of the "New window" button.
It opens a `Placement=Top` flyout showing the current connection's **directory**
(`ChatStore.ServerDirectory`), **URL** and **password**, each as a selectable `TextBlock`
(`IsTextSelectionEnabled=true`) plus a copy button (`Symbol.Copy`; `SessionSidebar.CopyToClipboard`
→ `Clipboard.SetContent` + a success toast).
The password is **masked by default** (`MaskPassword` → fixed `••••••••`, or "None" when the server
has no password) with an eye toggle (`Symbol.View`, `SessionSidebar.ShowPassword` ref) to reveal it;
`ShowPassword` resets to false when the flyout closes (`@Closed`), and the eye + copy buttons are
hidden entirely when the server has no password (`ConnectionPassword.Length > 0` gates their
`Visibility`).
The values come from `ChatStore.ConnectionUrl`/`ConnectionPassword`, reactive fields set in
`ChatStore.Configure` (password resolved with the `OPENCODE_SERVER_PASSWORD` env-var fallback so
it's the effective one).