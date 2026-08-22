# AGENTS.md

Guidance for AI coding agents working in this repository.
This is general context about the project and environment — not task-specific instructions.

## Contribution guideline (AGENTS.md + agents-doc/)

**Keep AGENTS.md and every `agents-doc/*.md` current.**
When any fact changes (project restructure, new/renamed files or folders that alter the source
layout, a changed build/run command, a new service or dependency, updated package/SDK versions),
update the owning file to match — do not leave it outdated.

**What goes where:**

- **AGENTS.md holds rules** agents must follow, plus the most important facts needed for (almost)
  every task: project identity, stack constraints, build/run, and safe-operation rules.
- **`agents-doc/*.md` holds need-to-know detail**, read on demand when a task touches that area.
  See "Reference documentation" below for what each file covers and when to read it.
- Keep the split consistent when something changes: rules stay in AGENTS.md, detail lives in
  agents-doc/. Update both together when a rule's underlying detail changes.

**Removed features are removed everywhere.**
When a feature is removed, delete every mention of it from code, comments, docs, and scripts —
including AGENTS.md and agents-doc/. Do **not** document that the old feature existed
(no "the old X was replaced by Y", no "X is no longer supported"). Describe only what exists today.

**Keep lines under 150 characters (AGENTS.md and agents-doc/*.md).**
Break long lines at natural sentence/clause boundaries. Use sub-bullets for dense sections
instead of single massive paragraphs. This keeps diffs clean and the file scannable.
This does not apply to source code — follow the project's existing code style.
Before finishing any change that touches one of those files, run
`scripts/validate-markdown-lines.ps1` (Windows) or `scripts/validate-markdown-lines.sh`
(Linux/macOS) — it exits non-zero when any line exceeds the limit.

## Reference documentation (agents-doc/)

Everything an agent needs on a need-to-know basis lives in `agents-doc/`. Each entry states
**why the file is relevant** and **when to read it**. Read the file for your area before editing
that code, and keep it up to date alongside AGENTS.md (see "Contribution guideline").

- **`agents-doc/opencode-server.md`** — the `opencode serve` REST + SSE protocol: auth, startup
  health, the `/event` stream, session create/list/rename/abort, title auto-naming, subagent
  (task-tool) sessions, the permission + question APIs, session status/errors, MCP API,
  unhandled events, and serve flags/port probing.
  _Read before_ working on `OpencodeClient`, `ChatStore.Apply`, SSE event handling,
  permissions/questions, MCP, or `ServeProcess`.
- **`agents-doc/session-state.md`** — client-side per-session state: the ChatStore/SessionStore
  split, send-while-busy modes + the send-mode button, interrupt, revert/undo + the per-message
  revert flyout, image attachments, fork (per-message + full), auto-retry + continue cards, and
  chat autoscroll.
  _Read before_ editing chat send/revert/fork/autoscroll behavior or `SessionStore`.
- **`agents-doc/session-sidebar.md`** — the sidebar: no-rebuild model, directory groups + git
  branch, folder actions, the Open Folder button, the connection-details flyout, and the
  session/busy/unread indicators.
  _Read before_ editing `SessionSidebar` or sidebar state in `ChatStore`.
- **`agents-doc/quickmarkup.md`** — QuickMarkup gotchas (Init(), reactivity, keyed foreach,
  two-way binding), version notes, and the skill location.
  _Read before_ writing or editing QuickMarkup markup (the "Always load the skill" rule below
  also applies).
- **`agents-doc/settings.md`** — the data-driven settings system: `SettingsStore`, the `Specs`
  registry, `settings.json` persistence + watcher, the "add a setting" recipe, and the settings
  in use (editor command, send mode, code font / `CodeFonts` / `SystemFonts`).
  _Read before_ editing `SettingsStore`, `SettingsPage`, `CodeFonts`, `SystemFonts`, or when
  adding a setting.
- **`agents-doc/polyfills.md`** — the per-OS `FolderPicker` polyfills + `WindowsHelper` routing:
  file conventions, API shape, csproj gating, Linux D-Bus and macOS AppKit flows.
  _Read before_ touching folder pickers, `WindowsHelper`, or any `UnoVibe/Polyfills/*` file.
- **`agents-doc/notifications.md`** — desktop notifications: the `Notifications` façade, the
  shared toast path, WASDK/Linux/macOS delivery, focus gating, and the no-op targets.
  _Read before_ editing `Services/Notifications.cs`, notification wiring, or the notification
  polyfills.
- **`agents-doc/connect-page.md`** — the ConnectPage flow: two-column layout + compact mode,
  one-click Open Folder, folder security/passwords, `recent.json` persistence, and the
  server-password prompt.
  _Read before_ editing `ConnectPage`, `RecentConnectionsStore`, or the connect/serve flows.
- **`agents-doc/responsive-layout.md`** — the compact-window layout system: `MainPage`'s
  `IsCompact`/`IsSidebarView` switching and the compact reflows in `ChatHeader`/`ChatComposer`.
  _Read before_ changing page grid layouts or compact breakpoints.
- **`agents-doc/markdown-rendering.md`** — `MarkdownView`: Markdig pipeline, merged flow blocks,
  streaming reconcile, PlainMode, HTML/table handling, ColorCode highlighting, inline code.
  _Read before_ editing `MarkdownView`, `MessageTextPart`, `CodeHighlighter`, or `AccentPalette`.
- **`agents-doc/tool-views.md`** — tool-call rendering: `ToolView*` cards, `DiffView`/`CodeView`,
  and the `apply_patch` metadata → `PatchJson` flow.
  _Read before_ editing `UnoVibe/Controls/ToolViews/*`, `DiffView`, `CodeView`, or apply_patch
  parsing.
- **`agents-doc/suggest-box.md`** — `SuggestBox`/`SuggestionBoxController`: trigger parsing,
  providers (commands/skills/files), the legacy vs `/api/*` route skew, focus management.
  _Read before_ editing `SuggestBox`, `SuggestionProviders`, or the suggestion fetch helpers.
- **`agents-doc/referenced-projects.md`** — Linux-only upstream source checkouts (QuickMarkup,
  Uno, opencode) and the Uno TextBox key-processing quirk SuggestBox works around.
  _Read when_ you need upstream source answers, or on a Windows dev machine (those paths absent).
- **`agents-doc/dev-environment.md`** — Linux dev-machine runtime notes: the day-to-day 4196
  server, the app log path, and the `unovibe` CLI caveat.
  _Read when_ running/debugging/logging the app on the Linux dev machine.

## What This Project Is

**UnoVibe** is a desktop chat client for [opencode](https://opencode.ai) built with Uno Platform.
It talks to an `opencode serve` HTTP server over a minimal REST + SSE protocol and renders the
chat session (messages, session list, tool views, questions) in a Skia-rendered desktop UI.

High-level goals/design:
- App should be **self-contained**: it can launch its own local `opencode serve` from a
  user-picked folder, or connect to an existing server.
- Uses **QuickMarkup** (declarative reactive UI DSL, Vue-inspired) instead of XAML.
- Desktop-only: the **Skia** target (`net10.0-desktop`) everywhere, and on Windows additionally a
  **WinUI** target (`net10.0-windows10.0.26100.0`). Android/iOS/WebAssembly targets are commented
  out in the csproj.

## Tech Stack

- **Uno Platform** via `Uno.Sdk` (see `global.json`, currently `6.6.42`).
  Do **not** bump individual Uno package versions — update the SDK version in `global.json` instead.
- **.NET 10** (`dotnet --version` → `10.0.110`). Targets: `net10.0-desktop` (Skia) and, on
  Windows only, `net10.0-windows10.0.26100.0` (WinUI) — the csproj gates the second TFM behind
  `$(OS) == 'Windows_NT'`.
- **QuickMarkup** `0.1.23` (versions pinned in `Directory.Packages.props`, currently a
  locally-packed build of the upstream `wt-master` repo): `QuickMarkup.Uno` for non-Windows
  targets, **`QuickMarkup.WinUI`** + **`Microsoft.WindowsAppSDK`** for `net10.0-windows`.
  Uses central package management.
- Only external package references: `QuickMarkup.Uno`, `Markdig`, and `ColorCode.Core`
  (plus `QuickMarkup.WinUI`, `Microsoft.WindowsAppSDK`, and `Microsoft.Graphics.Win2D` on the
  Windows target). Everything else comes from the Uno.Sdk implicit packages.
  `SkiaSharp` is used by `Services/SystemFonts.cs` but **not referenced directly** — it comes
  transitively from Uno's Skia host, so desktop targets get it with no added dependency.
- Build uses `Uno.SingleProject`; `EmitCompilerGeneratedFiles=true` so generated source lands
  under `UnoVibe/obj/<tfm>/generated/...`.

### Native AOT constraints

`<PublishAot>true</PublishAot>` is set in the csproj, so reflection-based JSON is unavailable.
All JSON (de)serialization must go through the source-generated **`Services/AppJsonContext.cs`**
(`AppJsonContext.Default.X`): `JsonSerializer.Deserialize/Serialize` with a `JsonTypeInfo`, and
the `PostAsJsonAsync`/`PatchAsJsonAsync` overloads taking a `JsonTypeInfo`.

- Do NOT add new reflection-based `JsonSerializer.Deserialize<T>(..., JsonSerializerOptions)`
  calls, anonymous/Dictionary request bodies, or `JsonSerializerOptions` fields.
- Every request body and persisted model is a named class registered in `AppJsonContext`
  (opencode request DTOs like `CreateSessionRequest`/`SendPromptRequest`/`EmptyRequest` live in
  that file too).

**Uno platform quirks:**

- `Windows.Storage.Streams.DataReader.LoadAsync` is **not implemented in Uno** (Uno0001) —
  read `IRandomAccessStream` via `AsStreamForRead()` instead
  (Uno's own `Win32ClipboardExtension` uses that pattern).
- `ComboBox.DisplayMemberPath`/`SelectedValuePath` are **banned** —
  Uno resolves the item property for those via a reflection-driven `BindingPath` that NativeAOT
  trimming breaks (the model combo rendered an empty label and dead selection under AOT only).
  Use `ItemTemplate` + an object-based `SelectedItem` binding instead
  (the model combo binds `SelectedItem` to the reactive computed `SessionStore.SelectedModelOption`,
  resolved from `Router.ModelOptions` via `.Reactive.FirstOrDefault(...)`).

### Compile-time OS constants

The csproj defines these constants for `#if`-gated OS-specific code (all conditions are scoped
to the active TFM via `$(TargetFramework)`, so cross-target builds can't leak one target's OS
into another):

- **`DESKTOP_WINDOWS` / `DESKTOP_LINUX` / `DESKTOP_MACOS`** — the OS of a `net10.0-desktop`
  (Skia) build only; **never** defined on the `net10.0-windows10.0.26100.0` TFM.
- **`WINDOWS`** — any Windows-targeted build: a `net10.0-desktop` build with a `win-*` RID or on
  a Windows host, plus the `net10.0-windows10.0.26100.0` TFM (where the .NET SDK also
  auto-defines it).
- **`WASDK`** — the `net10.0-windows10.0.26100.0` (WinAppSDK) target only.

Resolution order (per TFM): an explicit `-r` (cross-publish, e.g. `dotnet publish -r win-x64`
from Linux) identifies the target OS directly; with no RID (F5 / `dotnet run` / the
`build-desktop` task) the build host IS the run host, so the conditions fall back to
`[MSBuild]::IsOSPlatform(...)`. Verified: F5 on Linux → `DESKTOP_LINUX` only; desktop
`-r win-x64`/`win-arm64` → `DESKTOP_WINDOWS`+`WINDOWS`; desktop `-r osx-arm64` → `DESKTOP_MACOS`;
desktop `-r linux-x64` → `DESKTOP_LINUX`; `net10.0-windows10.0.26100.0` (any RID) →
`WASDK`+`WINDOWS`.

This replaces runtime `OperatingSystem.IsWindows()/IsMacOS()/IsLinux()` dispatch. The
`net10.0-desktop` build's OS still comes from `DESKTOP_*` where Skia/WinUI behavior differs; pure
OS behaviors that are identical on WinAppSDK use the broader `WINDOWS` guard instead
(`Services/FolderLauncher.cs` file-manager/editor/terminal/`PATHEXT` Windows branches).

## Windows Build (WinUI) Conventions

The Windows **WinUI** target is supported and should be kept compilable, so follow these
conventions when writing cross-target code. On a Linux dev environment you **cannot** build
`net10.0-windows` — there's no way to compile/verify the Windows target here — so write code
that follows the portable forms below to avoid breaking Windows later. (On Windows the dev
machine also lacks the reference clones in `agents-doc/referenced-projects.md` — Linux-only paths.)

- **Windows has no `Thickness` two-value constructor.** `new Thickness(1, 2)` (horizontal/vertical)
  compiles under Uno but not under the real WinUI/WinRT `Thickness` — always write all four values:
  `new Thickness(1, 2, 1, 2)`.
- **Windows has no implicit `Brush` conversion.** `Brush b = Colors.Transparent;` compiles under
  Uno (implicit conversion) but not WinUI — construct the brush explicitly, e.g.
  `new SolidColorBrush(Colors.Transparent)`.
- **Windows APIs that need an HWND to appear.** Dialogs/pickers (e.g. `FolderPicker`,
  `FileOpenPicker`) and similar WinRT APIs must be associated with a window handle on Windows —
  calling `PickSingleFolderAsync`/`PickSingleFileAsync` **without** `InitializeWithWindow.Initialize`
  crashes the app on the Windows target. Uno's Skia target does this internally, so use the
  `UnoVibe.WindowsHelper` wrapper instead of `WinRT.Interop` directly:
  `WindowsHelper.InitializeWithWindow(picker, window)` — it takes the app `Window` (resolving the
  `hwnd` via `window.AppWindow.Id` internally) and no-ops on non-WinUI targets via an internal
  `#if WASDK` guard. The `Window` is **always non-null** at call sites (never pass null — it must
  be set or the Windows target crashes). **Getting the `Window` at a picker call site**: the window
  flows through the QuickMarkup provide/inject context — `MainPage` declares
  `provide Window HostWindow = null` (filled by `WindowController.ShowMain` via
  `ProvideWindow(Window)`), and pages/components that open pickers `inject Window HostWindow` and
  pass it to `WindowsHelper.InitializeWithWindow`. Callers like `SessionStore.PickImageAsync(Window)`
  take it as a parameter. `ConnectPage` reaches it through its own `Controller.Window` instead.
  Folder picking routes through `WindowsHelper.PickFolderAsync(window, startPath)` — per-target
  WASDK / polyfill / classic routing — see `agents-doc/polyfills.md`.
- Since the Windows target is planned/supported, prefer these portable forms whenever convenient;
  on Linux just write the forms above — the goal is code that compiles on both targets.

Desktop notifications bridge the sidebar indicators to native toasts via a platform-dispatching
façade (`Services/Notifications.cs`) whose callers need no `#if` guards — see
`agents-doc/notifications.md`.

## How to Build & Run

At the start of a new session, verify the dev server is actually running before assuming it is —
the machine may have been restarted. Check with:

```bash
ps aux | grep "opencode serve" | grep -v grep
```

If `http://localhost:4196` is not up, start it manually (or use the ConnectPage "Local server" flow):

```bash
nohup opencode serve --port 4196 > /mnt/LinuxProgramData/tmp/opencode/serve_dev.log 2>&1 & disown
```

```bash
# Build (desktop target)
dotnet build UnoVibe/UnoVibe.csproj -f net10.0-desktop

# Run (this is the dev-workflow launch; app forks and stays in background)
cd /mnt/Data/Codes/UnoVibe/wt-develop # or current worktree
nohup dotnet run --project UnoVibe/UnoVibe.csproj -f net10.0-desktop --no-build -- http://localhost:4196 \
  > /mnt/LinuxProgramData/tmp/opencode/app_run.log 2>&1 & disown

# Relaunch (kill only the app; leave servers alone)
pkill -9 -x UnoVibe
```

### Positional argument

The app takes a **single positional argument** (VSCode `code path` style):
- A folder path runs `opencode serve` there.
- An `http(s)://` URL connects to an existing server.
- With no argument it shows `ConnectPage` (connect to existing server, or pick a folder and run
  `opencode serve` there).

Optional `--password [value]` overrides the default password behavior (folder: generated strong
password; server: no password):
- A bare `--password` uses `OPENCODE_SERVER_PASSWORD`.
- `--password ""` means no password.
- `--password <value>` uses the given password.
- A folder path that resolves to a file fails the launch (error + exit 1); a missing folder is
  created.

### Shell safety

Note: `pkill -f "opencode serve ..."` and similar broad patterns can hang the shell session in this
environment — prefer `pkill -9 -x <exact-name>` or `pkill -f` with a unique exact port, and check
with `ps aux | grep "opencode serve"` afterward.

Do **not** run `find /` (filesystem-wide searches) — they are extremely slow and time out the
shell. Use targeted `find` under a specific directory (e.g. `find ~ -name recent.json`), the
Glob/Grep tools, or known absolute paths.

### CRITICAL — do NOT kill the dev server

**do NOT kill the dev `opencode serve` (port 4196) during this chat session:**
this opencode session itself is served by that instance, so killing it terminates the chat and the
command. If a server-side change (e.g. `small_model`/auth/setting edits in
`~/.config/opencode/opencode.jsonc`, global opencode.json, or plugins) requires a restart to take
effect, ask the user to restart it themselves rather than running `pkill -9 -x opencode`
(or dropping into the session's own TUI to `/session` restart).

### CRITICAL — do NOT kill, relaunch, or auto-test the app

**do NOT kill, relaunch, or auto-test the UnoVibe app:**
the user is talking to this opencode session **through the running UnoVibe app**, so killing it
(`pkill -9 -x UnoVibe` or otherwise) terminates the user's view of the chat.

- Do **not** launch the app yourself.
- Do **not** test it yourself via app-mcp (`uno_app_start`, `uno_app_visualtree_snapshot`,
  `uno_app_get_screenshot`, pointer/key input, etc.).
- After making changes, **return to the user**: summarize what changed and state clearly what the
  user should test manually (e.g. "launch/relaunch the app and check X").
- Only use app-mcp to investigate when the **user explicitly asks you to**
  (e.g. "inspect the UI", "take a screenshot", "why is X not rendering").

## Source Layout

- `UnoVibe/Pages/Connect/` — `ConnectPage` plus its page-local panels: `RecentListPanel`
  (the recent-connections card) and `ConnectPanel` (the start-a-session + folder-security column).
  The connect flow (serve launch, URL connect, password resolution) stays on the page.
  See `agents-doc/connect-page.md`.
- `UnoVibe/Pages/Main/` — `MainPage` plus `SessionSidebar` and `SettingsPage` (both only hosted
  by the main page; the settings panel is its modal overlay). See `agents-doc/responsive-layout.md`.
- `UnoVibe/Pages/Chat/` — `ChatPage` plus the page-local chat components: `ChatHeader`
  (title/rename/back/stats/usage), `ChatStatusArea` (status banner + subagent strip),
  `ChatMessageList` (message list, revert/retry/continue/permission cards, autoscroll),
  `ChatComposer` (image strip, input, send, mode/model/variant), and the message-rendering
  controls `MessageView`, `MessageTextPart`, `ModelPicker`, `SendMessageButton`.
  The chat page coordinates sends and provides the shared composer text (`Input`).
  See `agents-doc/session-state.md`, `agents-doc/tool-views.md`, `agents-doc/markdown-rendering.md`.
- `UnoVibe/Controls/` — reusable UI used across pages: `AppSymbolIcon`, `CodeHighlighter`
  (ColorCode-based syntax highlighting for fenced code blocks), `CodeView`/`DiffView`
  (line-numbered syntax-highlighted code and colored unified-diff views for tool cards),
  `FolderActions`, `MarkdownView` (Markdig-based markdown renderer with a markdown/plain toggle),
  `SuggestBox` (+ `SuggestionItem`, `SuggestionBoxController`), `SymbolExtemsion`,
  `ToolViews/*` (ToolView* render opencode tool calls).
  See `agents-doc/markdown-rendering.md`, `agents-doc/tool-views.md`, `agents-doc/suggest-box.md`.
- `UnoVibe/Services/` — core logic:
  - `OpencodeClient.cs` — minimal HTTP client for the opencode REST API; Basic-auth capable.
  - `ChatStore.cs` — the per-window **router** store.
    Owns the connection (client, serve process, SSE event pump), the sidebar state (sessions,
    directory groups, MCP servers), the shared settings options (modes/models/variants),
    the global permission/toast surfaces, and the per-session **`SessionStore` cache** (keyed by
    session id). `Active` (a reactive `SessionStore?` field) is the store for the currently-open
    session; switching sessions re-points it and raises `ActiveStoreChanged` (the chat page
    re-hooks the active store's message list on that event). Session-scoped SSE events are
    dispatched to the owning cached store; sessions never opened have no store, so only the
    sidebar maps are fed.
  - `SessionStore.cs` — a cached per-session store holding that session's messages,
    composer/model/variant/mode selection, usage/token/context stats, revert/redo state,
    retry card state, and pending-image attachments.
    Lazily created and loaded on first open (`LoadAsync`), then kept alive and reused on revisit
    (stale-while-revalidate `RefreshAsync` when not mid-turn), so switching away and back preserves
    the live message list. `Router` back-reference provides the shared client/options/status
    surfaces. Fields in its `[QuickMarkup]` header are the reactive references the chat page binds
    to via `Store.Active.X`. See `agents-doc/session-state.md`.
  - `EventStreamReader.cs` — reads the SSE `/event` stream.
  - `AppJsonContext.cs` — the source-generated `System.Text.Json` context (AOT-mandated; see
    "Native AOT constraints") plus the named opencode request DTOs it registers.
  - `ServeProcess.cs` — launches `opencode serve --port <free>` in a folder, waits for health.
    Password: null → generated strong password, "" → unsecured, non-empty → used.
  - `StartupArgs.cs` — command-line parsing (`LaunchKind`/`PasswordMode`):
    the single positional folder-or-URL argument plus the `--password` flag;
    `ResolveFolderPassword`/`ResolveServerPassword` map to the per-mode defaults.
  - `SuggestionProviders.cs` — the `ISuggestionProvider` implementations for `SuggestBox`
    (namespace `UnoVibe.Controls`). See `agents-doc/suggest-box.md`.
  - `SettingsStore.cs` — app settings: typed static values, a `Specs` registry for the
    data-driven settings page, `settings.json` persistence, and a cross-process file watcher.
    See `agents-doc/settings.md`.
- `UnoVibe/Models/` — DTOs (`MessageItem`, `SessionInfo`, `ModelOption`, `ToolView*` item types,
  etc.), plus the settings page's reactive row model (`SettingsEntry`).
- `UnoVibe/Pages/Main/SettingsPage.cs` — the settings panel (modal overlay), rendered from
  `SettingsStore.Specs`. See `agents-doc/settings.md`.
- `App.xaml.cs` — startup routing: parses `StartupArgs` (`App.CreateWindow`), fails the launch on
  a file-target, hands folder/URL targets to `ConnectPage` via `WindowController.ShowConnect(startup)`,
  which runs the connect flow and swaps to `MainPage` on success.

## QuickMarkup

**Always load the skill** when editing QuickMarkup UI:
`.agents/skills/quickmarkup/SKILL.md` (a copy of the one from the QuickMarkup repo).

Rules learned the hard way (full detail + version notes in `agents-doc/quickmarkup.md`):

- A `[QuickMarkupConstructor]` method **must call `Init()`** (usually first) or the UI tree never
  builds.
- Only `Reference<T>` fields declared in the `[QuickMarkup("""...""")]` header are reactive.
  A plain `ObservableCollection.Count` in an `if` condition is NOT reactive; use `ReactiveList<T>`
  or `.Reactive.Count` (and with `&&` short-circuiting, read at least one Reference first).
- **Keyed `foreach`**: add a key (`` `group.Directory` ``, `` `s.Id` ``, `` `m.Name` ``) only for
  collections rebuilt via Clear+re-Add (message list, sidebar groups/sessions/MCP, subagent strip);
  leave incrementally-mutated collections (e.g. `Message.Parts`, `PendingImages`) deliberately
  unkeyed.
- Two-way binding is `` Property<=>`Var` ``. `CheckBox.IsChecked` is `bool?` — binding it to a
  `bool` field will not compile; use `ToggleSwitch` (`IsOn` is `bool`) instead.
- Values in markup are not quoted; use backticks for C# expressions, `<>...</>` for
  collection-typed properties, `if (`expr`) { }` for conditional children.

## Referenced / Cloned Projects

The upstream source checkouts (QuickMarkup, Uno, opencode) exist only on the Linux dev machine —
a Windows dev environment does **not** have them, so don't assume those paths are available there.
See `agents-doc/referenced-projects.md` for the paths, what each is for, and the Uno TextBox
key-processing quirk SuggestBox works around.

## CONTRIBUTION RULES AND BANNED PATTERNS

This applies to new and changed codes.

### `Router.ConnectionStatus` message is not for error.

Don't set error message to `Router.ConnectionStatus` for failure. Its rendering is too small and user can't read it. It's just have enough space for `Connected` string.

Instead: recommend to do toasts.