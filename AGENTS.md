# AGENTS.md

Guidance for AI coding agents working in this repository.
This is general context about the project and environment — not task-specific instructions.

**Keep this file (project AGENTS.md) current.**
When any fact here changes (e.g. project restructure, new/renamed files or folders that alter the source layout,
a changed build/run command, a new service or dependency, updated package/SDK versions), update AGENTS.md to match —
do not leave it outdated.

**Removed features are removed everywhere.**
When a feature is removed, delete every mention of it from code, comments, docs, and scripts — including this file.
Do **not** document that the old feature existed (no "the old X was replaced by Y", no "X is no longer supported").
Describe only what exists today.

**Keep lines under 150 characters (AGENTS.md only).**
Break long lines at natural sentence/clause boundaries. Use sub-bullets for dense sections
instead of single massive paragraphs. This keeps diffs clean and the file scannable.
This does not apply to source code — follow the project's existing code style.

## What This Project Is

**UnoVibe** is a desktop chat client for [opencode](https://opencode.ai) built with Uno Platform.
It talks to an `opencode serve` HTTP server over a minimal REST + SSE protocol and renders the chat session
(messages, session list, tool views, questions) in a Skia-rendered desktop UI.

High-level goals/design:
- App should be **self-contained**: it can launch its own local `opencode serve` from a user-picked folder,
  or connect to an existing server.
- Uses **QuickMarkup** (declarative reactive UI DSL, Vue-inspired) instead of XAML.
- Desktop-only: the **Skia** renderer target (`net10.0-desktop`) everywhere, and on Windows
  additionally a **WinUI** target (`net10.0-windows10.0.26100.0`). Android/iOS/WebAssembly
  targets are commented out in the csproj.

## Tech Stack

- **Uno Platform** via `Uno.Sdk` (see `global.json`, currently `6.6.42`).
  Do **not** bump individual Uno package versions — update the SDK version in `global.json` instead.
- **.NET 10** (`dotnet --version` → `10.0.110`). Targets: `net10.0-desktop` (Skia) and, on Windows
  only, `net10.0-windows10.0.26100.0` (WinUI) — the csproj gates the second TFM behind
  `$(OS) == 'Windows_NT'`.
- **QuickMarkup** `0.1.23` (versions pinned in `Directory.Packages.props`, currently a
  locally-packed build of the upstream `wt-master` repo): `QuickMarkup.Uno` for non-Windows
  targets, **`QuickMarkup.WinUI`** + **`Microsoft.WindowsAppSDK`** for `net10.0-windows`.
  Uses central package management.
- Only external package references: `QuickMarkup.Uno`, `Markdig`, and `ColorCode.Core`
  (plus `QuickMarkup.WinUI` and
  `Microsoft.WindowsAppSDK` on the Windows target). Everything else comes from the Uno.Sdk
  implicit packages.
- Build uses `Uno.SingleProject`; `EmitCompilerGeneratedFiles=true` so generated source lands under
  `UnoVibe/obj/<tfm>/generated/...`.

### Native AOT constraints

`<PublishAot>true</PublishAot>` is set in the csproj, so reflection-based JSON is unavailable.
All JSON (de)serialization must go through the source-generated **`Services/AppJsonContext.cs`**
(`AppJsonContext.Default.X`): `JsonSerializer.Deserialize/Serialize` with a `JsonTypeInfo`, and the
`PostAsJsonAsync`/`PatchAsJsonAsync` overloads taking a `JsonTypeInfo`.

- Do NOT add new reflection-based `JsonSerializer.Deserialize<T>(..., JsonSerializerOptions)` calls,
  anonymous/Dictionary request bodies, or `JsonSerializerOptions` fields.
- Every request body and persisted model is a named class registered in `AppJsonContext`
  (opencode request DTOs like `CreateSessionRequest`/`SendPromptRequest`/`EmptyRequest` live in that file too).

**Uno platform quirks:**

- `Windows.Storage.Streams.DataReader.LoadAsync` is **not implemented in Uno** (Uno0001) —
  read `IRandomAccessStream` via `AsStreamForRead()` instead
  (Uno's own `Win32ClipboardExtension` uses that pattern).
- `ComboBox.DisplayMemberPath`/`SelectedValuePath` are **banned** —
  Uno resolves the item property for those via a reflection-driven `BindingPath` that NativeAOT trimming breaks
  (the model combo rendered an empty label and dead selection under AOT only).
  Use `ItemTemplate` + an object-based `SelectedItem` binding instead
  (the model combo binds `SelectedItem` to the reactive computed `SessionStore.SelectedModelOption`,
  resolved from `Router.ModelOptions` via `.Reactive.FirstOrDefault(...)`).

### Compile-time OS constants

The csproj defines these constants for `#if`-gated OS-specific code (all conditions are scoped
to the active TFM via `$(TargetFramework)`, so cross-target builds can't leak one target's OS
into another):

- **`DESKTOP_WINDOWS` / `DESKTOP_LINUX` / `DESKTOP_MACOS`** — the OS of a `net10.0-desktop`
  (Skia) build only; **never** defined on the `net10.0-windows10.0.26100.0` TFM.
- **`WINDOWS`** — any Windows-targeted build: a `net10.0-desktop` build with a `win-*` RID or on a
  Windows host, plus the `net10.0-windows10.0.26100.0` TFM (where the .NET SDK also auto-defines it).
- **`WASDK`** — the `net10.0-windows10.0.26100.0` (WinAppSDK) target only.

Resolution order (per TFM): an explicit `-r` (cross-publish, e.g. `dotnet publish -r win-x64`
from Linux) identifies the target OS directly; with no RID (F5 / `dotnet run` / the
`build-desktop` task) the build host IS the run host, so the conditions fall back to
`[MSBuild]::IsOSPlatform(...)`. Verified: F5 on Linux → `DESKTOP_LINUX` only; desktop
`-r win-x64`/`win-arm64` → `DESKTOP_WINDOWS`+`WINDOWS`; desktop `-r osx-arm64` → `DESKTOP_MACOS`;
desktop `-r linux-x64` → `DESKTOP_LINUX`; `net10.0-windows10.0.26100.0` (any RID) → `WASDK`+`WINDOWS`.

This replaces runtime `OperatingSystem.IsWindows()/IsMacOS()/IsLinux()` dispatch. The
`net10.0-desktop` build's OS still comes from `DESKTOP_*` where Skia/WinUI behavior differs; pure OS behaviors
that are identical on WinAppSDK use the broader `WINDOWS` guard instead
(`Services/FolderLauncher.cs` file-manager/editor/terminal/`PATHEXT` Windows branches).

## Windows Build (WinUI) Conventions

The Windows **WinUI** target is supported and should be kept compilable, so follow these conventions when writing cross-target code. On a Linux dev environment you **cannot** build `net10.0-windows` — there's no way to compile/verify the Windows target here — so write code that follows the portable forms below to avoid breaking Windows later. (On Windows the dev machine also lacks the reference clones listed in "Referenced / Cloned Projects" — those are Linux-only paths.)

- **Windows has no `Thickness` two-value constructor.** `new Thickness(1, 2)` (horizontal/vertical) compiles under Uno but not under the real WinUI/WinRT `Thickness` — always write all four values: `new Thickness(1, 2, 1, 2)`.
- **Windows has no implicit `Brush` conversion.** `Brush b = Colors.Transparent;` compiles under Uno (implicit conversion) but not WinUI — construct the brush explicitly, e.g. `new SolidColorBrush(Colors.Transparent)`.
- **Windows APIs that need an HWND to appear.** Dialogs/pickers (e.g. `FolderPicker`, `FileOpenPicker`) and similar WinRT APIs must be associated with a window handle on Windows — calling `PickSingleFolderAsync`/`PickSingleFileAsync` **without** `InitializeWithWindow.Initialize` crashes the app on the Windows target. Uno's Skia target does this internally, so use the `UnoVibe.WindowsHelper` wrapper instead of `WinRT.Interop` directly: `WindowsHelper.InitializeWithWindow(picker, window)` — it takes the app `Window` (resolving the `hwnd` via `window.AppWindow.Id` internally) and no-ops on non-WinUI targets via an internal `#if WASDK` guard. The `Window` is **always non-null** at call sites (never pass null — it must be set or the Windows target crashes). **Getting the `Window` at a picker call site**: the window flows through the QuickMarkup provide/inject context — `MainPage` declares `provide Window HostWindow = null` (filled by `WindowController.ShowMain` via `ProvideWindow(Window)`), and pages/components that open pickers `inject Window HostWindow` and pass it to `WindowsHelper.InitializeWithWindow`. Callers like `SessionStore.PickImageAsync(Window)` take it as a parameter. `ConnectPage` reaches it through its own `Controller.Window` instead.
  **Folder picker (`WindowsHelper.PickFolderAsync(window, startPath)`):** on the WASDK target the folder picker uses the Windows App SDK's `Microsoft.Windows.Storage.Pickers.FolderPicker` (relies on the `Microsoft.WindowsAppSDK` package's `StoragePickersContract`, not Uno) so the dialog can open at an **exact path** — the `startPath` (set via `SuggestedStartFolder`) is the current window path, i.e. `ChatStore.ServerDirectory` (the folder/directory shown in the window title). The new picker takes the `WindowId` (`window.AppWindow.Id`) in its constructor, so it needs **no** `InitializeWithWindow`; its `PickSingleFolderAsync` returns a `PickFolderResult` (`.Path`), not a `StorageFolder`. The Linux and macOS desktop targets use the per-OS polyfills (see "Polyfills") which honor `startPath` too; only `DESKTOP_WINDOWS` (a Skia build running on Windows) falls back to the classic `Windows.Storage.Pickers.FolderPicker` + `InitializeWithWindow`, where `startPath` is ignored (that API has no exact-path control). Both call sites pass `Store.ServerDirectory` (`SessionSidebar`'s Open Folder / `ConnectPage`'s folder pick).
- Since the Windows target is planned/supported, prefer these portable forms whenever convenient; on Linux just write the forms above — the goal is code that compiles on both targets.

**Desktop notifications:**
`Services/Notifications.cs` bridges the chat sidebar indicators to native desktop notifications.
Every public method is a platform-dispatching façade, so callers need no `#if` guards.
- **The WASDK (WinUI), desktop-Linux and desktop-macOS (Skia) targets share ONE toast path**,
  guarded `#if WASDK || DESKTOP_LINUX || DESKTOP_MACOS`: the facade's `Show` builds the toast
  through the Windows App SDK's `AppNotificationBuilder` and hands it to
  `AppNotificationManager.Default.Show`. On WinUI those are the real
  `Microsoft.Windows.AppNotifications` types (implemented by Windows/WASDK, **not** by Uno);
  on Linux and macOS the polyfills (see "Polyfills") provide the same WASDK-named types — Linux
  sends the toast over the session D-Bus `org.freedesktop.Notifications` service (the interface
  `notify-send` uses), macOS via `terminal-notifier` (when on PATH) falling back to `osascript`.
  Only platform-specific bits (registration, the foreground check) stay behind their own
  `#if WASDK`/`#elif DESKTOP_LINUX`/`#elif DESKTOP_MACOS`.
- **Anywhere else:** no-op.

- `App.xaml.cs` calls `Notifications.Initialize()` in `OnLaunched` (before the first window;
  `Register()` also acquires the COM identity that lets an unpackaged app show toasts) and
  `Notifications.RegisterWindow(controller.Window)` after each window activates. Windows are
  stored as `Window` instances; the HWND is resolved on demand (`window.AppWindow.Id`) at check time.
- Fires for the same events the sidebar indicators show, from `ChatStore`:
  **background completion** (`ApplySessionStatus`, type idle + non-active session) and pending
  **question**/**permission** (`ApplyQuestionAsked`/`ApplyPermissionAsked`).
- **Focus gating is per-window:** a toast only fires when it carries info the user isn't already
  looking at. Each event passes its store's `Window` (`ChatStore.OwnerWindow`, set by
  `WindowController`); the gate compares only THAT window against `GetForegroundWindow()`.
  Background-session events always toast; active-session events (inline form/dialog on screen)
  are suppressed only while the owning window is the foreground window — a second window being
  focused does not silence another window's active-session toasts.
  On Linux the foreground check compares by **WM_CLASS** instead of a per-window handle (see the
  polyfill's doc — the real X11 window id is not exposed by Uno); on macOS it compares the
  frontmost app's **process id** via `NSWorkspace.frontmostApplication` (see the polyfill's doc),
  so *any* app window being focused suppresses the whole app's active-session toasts there.
- Session titles default to "New Chat" for the server's `"New session - <ISO>"`/`"Child session - <ISO>"`.
- Toast click-activation (switching to the session/replying) is **not** wired; the packaged
  `Package.appxmanifest` does not declare a `windows.toastNotificationActivation` activator, which
  is optional for showing toasts.

## Polyfills (platform folder pickers, desktop notifications)

`UnoVibe/Polyfills/{Linux,MacOS,Windows}/` holds one-file-per-OS polyfills of the WinAppSDK
**`Microsoft.Windows.Storage.Pickers.FolderPicker`** so every platform's folder dialog can open at
an **exact path** (`WindowsHelper.PickFolderAsync`'s `startPath` = the window's folder). Uno's
built-in `Windows.Storage.Pickers.FolderPicker` has no exact-path control, which the WASDK 2.0
picker (`SuggestedStartFolder` = the path) does have. See
https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.storage.pickers.folderpicker.
`WindowsHelper.PickFolderAsync` routes `#if WASDK` → WASDK picker, `#elif DESKTOP_LINUX ||
DESKTOP_MACOS` → the polyfill, else the classic fallback; callers pass the app `Window` and get a
`PickFolderResult?.Path`.

**Conventions for every polyfill file:**
- Each file starts and ends with a single `#if DESKTOP_LINUX` / `#if DESKTOP_MACOS` /
  `#if DESKTOP_WINDOWS` guard (one guard per file, matching the file's folder). The OS-specific
  code is what makes the file exist; the constants are defined per-TFM in the csproj (see
  "Compile-time OS constants"), so the guard just keeps disabled targets from compiling it.
- The class registers itself app-wide as `FolderPicker` via a top-level
  `global using FolderPicker = UnoVibe.Polyfills.<OS>.FolderPicker;` (the app never `#if`s this —
  callers use the one polyfilled name, and the WASDK/classic branches live inside
  `WindowsHelper`). `PickFolderResult` is a separate sibling file that owns its own
  `global using PickFolderResult = ...;` alias — an alias can appear in only one file, so never
  duplicate it.
- **API shape mirrors the WASDK picker** with two deliberate deviations: the constructor takes the
  app `Window` instead of a `WindowId`, and shared props are `SuggestedStartFolder` (exact path),
  `SuggestedFolder` (fallback), `SuggestedStartLocation` (a `PickerLocationId`), `CommitButtonText`,
  `Title`, `SettingsIdentifier` (macOS only). Methods: `PickSingleFolderAsync()` →
  `Task<PickFolderResult?>` (null = user cancelled; native failures throw). Getters/setters are
  plain .NET properties.
- **Keep the per-OS dependency footprint minimal** — the whole point of the polyfills is that a
  platform needs only what it already ships:
  - Desktop **Linux** talks to the XDG desktop portal over the session D-Bus (and the
    `org.freedesktop.Notifications` daemon for toasts), so it needs `Tmds.DBus.Protocol` +
    `Tmds.DBus.Generator` 0.92.0 — the same versions Uno's own X11 picker uses
    (`~/.nuget/packages/tmds.dbus.*`). C# interfaces are generated from the minimal XML files
    under `UnoVibe/Polyfills/Linux/dbus-interfaces/`
    (`org.freedesktop.portal.FileChooser.xml`, `org.freedesktop.portal.Request.xml`,
    `org.freedesktop.Notifications.xml`), wired to the `Tmds.DBus.Generator` source generator via
    csproj `AdditionalFiles` items (Namespace `UnoVibe.Polyfills.Linux.DBus`,
    `GenerateDBusTypes="true"`). The generated types land under
    `UnoVibe/obj/<tfm>/generated/Tmds.DBus.Generator/.../UnoVibe.Polyfills.Linux.DBus.g.cs`.
  - Desktop **macOS** drives `NSOpenPanel` through the Objective-C runtime with
    `[LibraryImport("libobjc.A.dylib")]` stubs in a `static partial` class (libobjc is part of
    macOS, so **no new dependency**). Because the interop source generator emits
    `unsafe` blocks, macOS desktop builds set `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in
    the csproj (Uno.Sdk enables unsafe only for the WinAppSDK target).
- **Gating in the csproj is per-OS**, matching the `DefineConstants` conditions above: the Tmds
  packages + `AdditionalFiles` are referenced only for desktop-Linux builds, and `AllowUnsafeBlocks`
  only for desktop-macOS builds (both via the same `$(TargetFramework)`, `$(RuntimeIdentifier)`, and
  `[MSBuild]::IsOSPlatform(...)` combination used for the constants — one ItemGroup each, not two).

**FolderPicker polyfill implementation notes:**
- The canonical example is `UnoVibe/Polyfills/Linux/FolderPicker.cs` (the old
  `SampleFolderPicker.cs` was deleted). Copy its `#if` + `global using` + ctor/props/Method shape
  for a future polyfill.
- **Linux D-Bus flow** (from Uno's `X11FolderPicker` in `uno/src/Uno.UI.Runtime.Skia.X11/...`):
  session D-Bus → `org.freedesktop.portal.Desktop` `/org/freedesktop/portal/desktop` → check
  `version >= 3` → subscribe `org.freedesktop.portal.Request`'s `Response` signal to the expected
  request path **before** calling `OpenFile` (portal race warning) → `OpenFileAsync("",
  title, options)` with `handle_token`, `accept_label`, `multiple=false`, `directory=true`,
  and `current_folder` = the start path NUL-terminated in UTF-8 → validate the returned request path
  equals the expected `.../request/<unique-names-sans-colons>/<handle_token>`, await the Response,
  take `uris[0]` → `new Uri(...).LocalPath`. Response codes: 0 = success, 1 = user cancelled. Empty
  `parent_window` means "no parent" — `WindowNative.GetWindowHandle` here only exposes the fake
  `AppWindow.Id`, so always pass `""`.
- **macOS flow**: build `NSOpenPanel` via `objc_msgSend` (`openPanel`,
  `setCanChooseDirectories:`, `setCanChooseFiles:`, `setAllowsMultipleSelection:`); if the start
  folder exists, `setDirectoryURL:` (from a `NSURL` built via `fileURLWithPath:`); run `runModal`
  → 1 = OK, 0 = cancelled → `URL` → `path` → `UTF8String` → `Marshal.PtrToStringUTF8`.
  **Always enqueue onto the main UI dispatcher before `runModal`** — AppKit crashes on reentrant
  presentation from an in-flight pointer handler, and `DispatcherQueue.GetForCurrentThread()` wraps
  Uno's main dispatcher (native `AppWindow.DispatcherQueue` is unimplemented on Skia), so call it
  from the UI thread. When off the UI thread (no queue), run inline as a best effort.

**Notifications polyfill (`UnoVibe/Polyfills/Linux/`):**
- **`AppNotifications.cs` supplies the WASDK API shape on Linux.** It defines
  `Microsoft.Windows.AppNotifications.AppNotificationManager`/`AppNotification` and
  `Microsoft.Windows.AppNotifications.Builder.AppNotificationBuilder` (block namespaces — two file-scoped
  namespaces in one file do not compile), so the facade's shared `#if WASDK || DESKTOP_LINUX`
  `AppNotificationBuilder` body (see "Desktop notifications") compiles unchanged on both targets.
  The builder/notification are plain text-line holders. `AppNotificationManager` owns the whole
  toast implementation: `Default` singleton, `Register()` (returns true; installs the no-op Xlib
  error handler), `Show` (fire-and-forget D-Bus send), and the `IsApplicationInForeground()` gate.
  Only the surface the app actually uses is provided, not the full WASDK API (tags, actions, ...).
- **D-Bus send flow (inside `AppNotificationManager`):** each toast opens its own session-bus
  `DBusConnection`, calls `org.freedesktop.Notifications` `/org/freedesktop/Notifications` `Notify`
  (app `UnoVibe`, empty icon, no actions/hints, `expire_timeout = -1`), fire-and-forget
  (`Show` → `_ = ShowAsync(...)`); failures are logged, never thrown (no daemon → ServiceUnknown).
- **Foreground gate (`IsApplicationInForeground`):** Uno exposes no real X11 window id
  (`AppWindow.Id` is a fake per-window counter on Skia), so the check reads the EWMH
  `_NET_ACTIVE_WINDOW` root property (walking child windows up to depth 2 for reparenting WMs) and
  matches the active window's `WM_CLASS` (res_name/res_class) against `Package.Current.Id.Name`
  (+ the entry-assembly name and "UnoVibe" as fallbacks) — every UnoVibe window shares that class
  (`X11XamlRootHost.SetWMClass`), so "any app window active" is the gate. Uses Xlib via
  `[DllImport("libX11.so.6")]` (ships with every X/XWayland server; no new package), opening a
  private per-check `XOpenDisplay` connection (Xlib handles aren't thread-safe). A no-op
  `XSetErrorHandler` is installed once (in `Register`) so Xlib's default exit-on-error handler can't
  kill the app when a window closes mid-check. Failures/unknowns return "not focused" → the toast
  fires (the WASDK path's conservative default too).
- **`Notifications` is the generated D-Bus client class** (namespace `UnoVibe.Polyfills.Linux.DBus`),
  not a polyfill file of its own; the manager obtains it via `var` to avoid shadowing.

**macOS notifications polyfill (`UnoVibe/Polyfills/MacOS/`):**
- **`AppNotifications.cs` supplies the WASDK API shape on macOS** (same block namespaces as the Linux
  file, so the facade's shared `#if WASDK || DESKTOP_LINUX || DESKTOP_MACOS` `AppNotificationBuilder`
  body compiles unchanged). The manager delivers each toast by spawning the platform tooling — no
  D-Bus, no app identity:
- **`terminal-notifier` first, `osascript` fallback.** `Show` runs the `terminal-notifier` CLI when
  it's on PATH (a PATH scan, cached including the negative; brew and the Ruby gem both put a launcher
  on PATH), waited up to 10s with a clean-exit requirement, so a broken install falls back. Otherwise
  it runs `osascript -e 'display notification "<body>" with title "<title>"'` — the same Standard
  Addition fallback opencode and Claude Code use. Both are fire-and-forget (`Process.Start` with
  `ArgumentList`, no shell); failures are logged, never thrown.
- **Why not the native UserNotifications framework?** `UNUserNotificationCenter` hard-asserts with
  `bundleProxyForCurrentProcess is nil` from a process that isn't part of a signed `.app` bundle, and
  the deprecated `NSUserNotificationCenter` silently needs a valid main-bundle identifier — while
  UnoVibe runs as a bare binary (`dotnet run` / plain `-r osx-arm64` publish). A future packaged
  `.app` could add a `UNUserNotificationCenter` path and keep these CLI routes as the unbundled fallback.
- **Attribution caveats:** with `terminal-notifier` the toast carries the tool's own identity and on
  newer macOS only shows when "terminal-notifier" is enabled in System Settings → Notifications; the
  `osascript` route is attributed to **Script Editor** (also enable it under System Settings). The
  `-sender` flag is deliberately **not** passed — it both fixes some Tahoe setups *and* hangs on
  Ventura/Sonoma (#301), so the polyfill stays on the default sender.
- **Foreground gate (`IsApplicationInForeground`):** macOS has no per-window foreground API reachable
  from an unbundled process, so the check compares the frontmost app's **process id**
  (`NSWorkspace.frontmostApplication` → `processIdentifier`, via the shared `UnoVibe.Polyfills.MacOS.ObjC`
  binder) with `Environment.ProcessId` — any app window focused counts as foreground. Failures/unknowns
  return "not focused" → the toast fires (the WASDK path's conservative default too).
- **Desktop-Windows has no notification polyfill yet** — desktop-Windows toasts remain no-ops today
  (its folder picker is polyfilled, notifications are not).

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
- With no argument it shows `ConnectPage` (connect to existing server, or pick a folder and run `opencode serve` there).

Optional `--password [value]` overrides the default password behavior (folder: generated strong password; server: no password):
- A bare `--password` uses `OPENCODE_SERVER_PASSWORD`.
- `--password ""` means no password.
- `--password <value>` uses the given password.
- A folder path that resolves to a file fails the launch (error + exit 1); a missing folder is created.

### Shell safety

Note: `pkill -f "opencode serve ..."` and similar broad patterns can hang the shell session in this environment —
prefer `pkill -9 -x <exact-name>` or `pkill -f` with a unique exact port, and check with
`ps aux | grep "opencode serve"` afterward.

Do **not** run `find /` (filesystem-wide searches) — they are extremely slow and time out the
shell. Use targeted `find` under a specific directory (e.g. `find ~ -name recent.json`), the
Glob/Grep tools, or known absolute paths.

### CRITICAL — do NOT kill the dev server

**do NOT kill the dev `opencode serve` (port 4196) during this chat session:**
this opencode session itself is served by that instance, so killing it terminates the chat and the command.
If a server-side change (e.g. `small_model`/auth/setting edits in `~/.config/opencode/opencode.jsonc`,
global opencode.json, or plugins) requires a restart to take effect, ask the user to restart it themselves
rather than running `pkill -9 -x opencode` (or dropping into the session's own TUI to `/session` restart).

### CRITICAL — do NOT kill, relaunch, or auto-test the app

**do NOT kill, relaunch, or auto-test the UnoVibe app:**
the user is talking to this opencode session **through the running UnoVibe app**, so killing it
(`pkill -9 -x UnoVibe` or otherwise) terminates the user's view of the chat.

- Do **not** launch the app yourself.
- Do **not** test it yourself via app-mcp (`uno_app_start`, `uno_app_visualtree_snapshot`,
  `uno_app_get_screenshot`, pointer/key input, etc.).
- After making changes, **return to the user**: summarize what changed and state clearly what the user
  should test manually (e.g. "launch/relaunch the app and check X").
- Only use app-mcp to investigate when the **user explicitly asks you to**
  (e.g. "inspect the UI", "take a screenshot", "why is X not rendering").

## Source Layout

- `UnoVibe/Pages/Connect/` — `ConnectPage` plus its page-local panels: `RecentListPanel`
  (the recent-connections card) and `ConnectPanel` (the start-a-session + folder-security column).
  The connect flow (serve launch, URL connect, password resolution) stays on the page.
- `UnoVibe/Pages/Main/` — `MainPage` plus `SessionSidebar` and `SettingsPage` (both only hosted
  by the main page; the settings panel is its modal overlay).
- `UnoVibe/Pages/Chat/` — `ChatPage` plus the page-local chat components: `ChatHeader`
  (title/rename/back/stats/usage), `ChatStatusArea` (status banner + subagent strip),
  `ChatMessageList` (message list, revert/retry/continue/permission cards, autoscroll),
  `ChatComposer` (image strip, input, send, mode/model/variant), and the message-rendering
  controls `MessageView`, `MessageTextPart`, `ModelPicker`, `SendMessageButton`.
  The chat page coordinates sends and provides the shared composer text (`Input`).
- `UnoVibe/Controls/` — reusable UI used across pages: `AppSymbolIcon`, `CodeHighlighter`
  (ColorCode-based syntax highlighting for fenced code blocks), `FolderActions`,
  `MarkdownView` (Markdig-based markdown renderer with a markdown/plain toggle),
  `SuggestBox` (+ `SuggestionItem`, `SuggestionBoxController`), `SymbolExtemsion`,
  `ToolViews/*` (ToolView* render opencode tool calls).
- `UnoVibe/Services/` — core logic:
  - `OpencodeClient.cs` — minimal HTTP client for the opencode REST API; Basic-auth capable.
  - `ChatStore.cs` — the per-window **router** store.
    Owns the connection (client, serve process, SSE event pump), the sidebar state (sessions, directory groups, MCP servers),
    the shared settings options (modes/models/variants), the global permission/toast surfaces,
    and the per-session **`SessionStore` cache** (keyed by session id).
    `Active` (a reactive `SessionStore?` field) is the store for the currently-open session;
    switching sessions re-points it and raises `ActiveStoreChanged`
    (the chat page re-hooks the active store's message list on that event).
    Session-scoped SSE events are dispatched to the owning cached store;
    sessions never opened have no store, so only the sidebar maps are fed.
  - `SessionStore.cs` — a cached per-session store holding that session's messages,
    composer/model/variant/mode selection, usage/token/context stats, revert/redo state,
    retry card state, and pending-image attachments.
    Lazily created and loaded on first open (`LoadAsync`), then kept alive and reused on revisit
    (stale-while-revalidate `RefreshAsync` when not mid-turn), so switching away and back preserves
    the live message list. `Router` back-reference provides the shared client/options/status surfaces.
    Fields in its `[QuickMarkup]` header are the reactive references the chat page binds to via
    `Store.Active.X`.
  - `EventStreamReader.cs` — reads the SSE `/event` stream.
  - `AppJsonContext.cs` — the source-generated `System.Text.Json` context (AOT-mandated; see Tech Stack)
    plus the named opencode request DTOs it registers.
  - `ServeProcess.cs` — launches `opencode serve --port <free>` in a folder, waits for health.
    Password: null → generated strong password, "" → unsecured, non-empty → used.
  - `StartupArgs.cs` — command-line parsing (`LaunchKind`/`PasswordMode`):
    the single positional folder-or-URL argument plus the `--password` flag;
    `ResolveFolderPassword`/`ResolveServerPassword` map to the per-mode defaults.
  - `SuggestionProviders.cs` — mock `ISuggestionProvider`s for `SuggestBox` (namespace `UnoVibe.Controls`).
  - `SettingsStore.cs` — app settings (see "Settings"): typed static values, a `Specs` registry for the
    data-driven settings page, `settings.json` persistence, and a cross-process file watcher.
- `UnoVibe/Models/` — DTOs (`MessageItem`, `SessionInfo`, `ModelOption`, `ToolView*` item types, etc.),
  plus the settings page's reactive row model (`SettingsEntry`).
- `UnoVibe/Pages/Main/SettingsPage.cs` — the settings panel (modal overlay), rendered from `SettingsStore.Specs`.
- `App.xaml.cs` — startup routing: parses `StartupArgs` (`App.CreateWindow`), fails the launch on a file-target,
  hands folder/URL targets to `ConnectPage` via `WindowController.ShowConnect(startup)`,
  which runs the connect flow and swaps to `MainPage` on success.

## QuickMarkup

**Always load the skill** when editing QuickMarkup UI:
`.agents/skills/quickmarkup/SKILL.md` (a copy of the one from the QuickMarkup repo).

Key gotchas learned the hard way:

- A `[QuickMarkupConstructor]` method **must call `Init()`** (usually first) or the UI tree never builds.
- Only `Reference<T>` fields declared in the `[QuickMarkup("""...""")]` header are reactive.
  Plain `ObservableCollection.Count` in an `if` condition is NOT reactive; with `&&` short-circuiting,
  at least one Reference must be read first to subscribe.
  QuickMarkup **0.1.21**: `ReactiveList<T>` (from `QuickMarkup.Infra.Collections`) makes
  `Count`/LINQ natively reactive — `PartItem.QuestionForm` uses it so the question form's `if` count
  check updates; the `.Reactive` extension (`myCollection.Reactive.Count`) is the
  ObservableCollection equivalent.
- **Keyed `foreach`**:
  - **Keyed**: the message `foreach`, the sidebar `DirectoryGroups`/`group.Sessions`/`McpServers` loops,
    and the `ActiveSubagents` strip are keyed (`` `group.Directory` ``/`` `s.Id` ``/`` `m.Name` ``)
    so QuickMarkup reuses elements across wholesale collection rebuilds (Clear+re-Add).
  - **Deliberately unkeyed**: `Message.Parts` and `PendingImages` — those are mutated incrementally
    (single Add/Remove), where a keyless ObservableCollection foreach is the O(1) fast path and
    a key adds reconcile overhead.
  - Add a key only for collections rebuilt via Clear+re-Add.
  - Since QuickMarkup **0.1.22-beta1** (local patch to `wt-master`), keyed reconciles are
    **incremental**: a single Add/Remove/Move only mounts/unmounts the affected block and moves
    surviving blocks in-place, so appending a message no longer unmounts/remounts the whole list
    (the flicker fix).
- Two-way binding is `` Property<=>`Var` ``.
  `CheckBox.IsChecked` is `bool?` and two-way binding it to a `bool` field will not compile —
  use `ToggleSwitch` (`IsOn` is `bool`) instead.
- Values in markup are not quoted; use backticks for C# expressions, `<>...</>` for collection-typed
  properties, `if (`expr`) { }` for conditional children.
- The QuickMarkup skill lives in `.agents/skills/quickmarkup/SKILL.md` (committed).
  The upstream source is at `/mnt/Data/Codes/QuickMarkup/wt-master/`.

## Referenced / Cloned Projects

These source checkouts exist only on the Linux dev machine — a Windows dev environment does **not**
have them cloned, so don't assume these paths (or the answers they give) are available there.

- **QuickMarkup source**: `/mnt/Data/Codes/QuickMarkup/wt-master/`
  — read this to understand markup syntax, the source generator, and what binds compile.
  Its own skill: `/mnt/Data/Codes/QuickMarkup/wt-master/.agents/skills/quickmarkup/SKILL.md`
  and `docs/qm-language.md`.
- **Uno Platform source**: `/mnt/Data/Codes/.GitHubClone/uno/`
  — useful for platform API behavior (e.g., X11 `FolderPicker` via desktop portal at `X11ApplicationHost.cs`;
  `FolderPicker.skia.cs` throws `NotSupportedException` if the extension is missing).
  Known Uno quirk (SuggestBox depends on it):
  TextBox's real key processing runs in `OnPostKeyDown` → `OnKeyDownSkia`, and `PostKeyDown` is raised
  **unconditionally** during `KeyDown` (`UIElement.RoutedEvents.cs`), so `e.Handled = true` in a
  `PreviewKeyDown` handler does NOT stop a handled Up/Down from moving the caret or a handled Enter
  from inserting a newline. SuggestBox works around it by cancelling the effects:
  `SelectionChanging` cancel (`_suppressArrowSelection`) for arrow keys while the flyout is open,
  and `BeforeTextChanging` cancel (`_blockStrayTextChange`, gated by `_programmaticTextChange`)
  for consumed Enter/Tab keys.
- **opencode source**: `/mnt/LinuxProgramData/tmp/opencode/opencode-src/`
  — server API/auth reference. Auth lives in `packages/opencode/src/server/auth.ts`.

## opencode Server Integration

### Auth

Basic auth `Authorization: Basic base64(username:password)`.
Env vars: `OPENCODE_SERVER_PASSWORD`, `OPENCODE_SERVER_USERNAME` (default username `opencode`).
Password empty/unset ⇒ unsecured.
**Every** endpoint requires auth when a password is set — including `GET /global/health` —
so health/startup probes must send the header too.

### Startup readiness

Poll `GET /global/health` until it returns `{"healthy":true,...}`.

### SSE events

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

### Session API

- `POST /session` — create; omit `title` so the server assigns a default and auto-generates a name
  (see "Titles").
- `GET /session` — list; **scoped by project + directory** — the server's `Session.list` filters by
  the instance's project ID, so sessions created in *other directories* of a different project
  (via `POST /session?directory=`) are NOT in the default list, which is why
  `ChatStore.RefreshSessionsAsync` additionally fetches `GET /session?directory=<path>` per opened
  sidebar folder and merges the results; but worktree directories of the same repo share the project
  ID and DO show up in the default list — see "SSE events" for why each such directory still needs
  its own event stream.
- `PATCH /session/:id` with `{ title }` — rename; this is how the TUI renames and how the server's
  title generator writes names.
- `POST /session/:id/abort` — interrupt the running turn.

### Titles

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

### Subagents

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

### apply_patch rendering

OpenAI-style models sometimes emit `apply_patch` (a single `patchText` with add/update/delete ops)
instead of `edit`/`write`. The tool returns `{ metadata: { diff, files, diagnostics } }` where `files`
is a per-file list
`{ filePath, relativePath, type: "add"|"update"|"delete"|"move", patch, additions, deletions, movePath }`
(source `packages/opencode/src/tool/apply_patch.ts`), landing in the tool part's `state.metadata`.

`ApplyToolState` captures both `metadata.diff` → `PartItem.Diff` (as before) and
`metadata.files` → `PartItem.PatchJson`; `MessageView` dispatches `tool == "apply_patch"` to
`ToolViewPatch` — a collapsible card ("← Patch <path>" / "← Patch N files", "Preparing patch..."
while in flight) that parses `PatchJson` via `ToolViewShared.ParsePatchFiles` and renders one
bordered block per file with a TUI-style label (`# Created`/`# Deleted`/`# Moved a → b`/
`← Patched <path>` + `(N+ M-)` counts) and the unified diff, falling back to the raw `Part.Diff`
when the server omits per-file metadata.

Mirrors the TUI's `ApplyPatch` (`routes/session/index.tsx`) and the web client's `patch` renderer
(`session-ui/src/components/message-part.tsx` + `apply-patch-file.ts`).

### Permission API

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

### Status / errors

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

### MCP API

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

### Unhandled events

`ChatStore.Apply` has `// TODO:` placeholder `case`s (with `break;`) for every other event the
server's `/event` stream emits:
`session.deleted/error/diff/idle/compacted`, `file.edited`, `file.watcher.updated`,
`todo.updated`, `lsp.updated`, `command.executed`,
`mcp.browser.open.failed`, `server.connected/heartbeat/instance.disposed`, `tui.toast.show`.

Handled: `session.created`/`session.updated`, `session.status`, `message.removed`,
`question.replied`/`question.rejected` (pending-attention counters),
`mcp.tools.changed` (→ `RefreshMcpStatusAsync`),
and `vcs.branch.updated` (→ `ChatStore.RefreshBranches`).

### Git branch in the sidebar

Each sidebar directory group shows its git branch (`⎇ <branch>`) after the folder name, from
`GET /vcs?directory=<path>` (`OpencodeClient.GetBranchAsync`, returns `{ branch, default_branch }`).
`ChatStore` keeps a `Dictionary<string, DirectoryGroup> _groupsByDirectory` index; `DirectoryGroup`
instances are **reused** (never recreated) across `ReconcileDirectoryGroups`, so the reactive
`Branch`/`IsExpanded` fields live on the object and survive refreshes with no re-seeding.
`RefreshBranches()` re-fetches every sidebar directory group's branch in place (no rebuild) and is
called after session refreshes and on the `vcs.branch.updated` SSE event.

The `session.next.*` streaming events exist in the schema but are not published by the current CLI
server. Implement a case and remove its TODO marker when adopting it.

### Serve flags & port probing

- `opencode serve` flags: `--port` default 0 (random), `--hostname` default `127.0.0.1`.
  Server instance is resolved per-request via the `x-opencode-directory` header,
  so it can be launched from any directory.
- Port probing at runtime should use a real bind (e.g., `TcpListener` on `127.0.0.1:0`,
  or Python `socket`); bash `shuf` can pick an occupied port.

### Interrupt / send-while-busy

`ChatStore.InterruptAsync()` calls `POST /session/:id/abort` (the server cancels the runner +
in-flight tools and marks aborted tool parts with `state.metadata.interrupted=true` and the assistant
message `error.name === "MessageAbortedError"`).

### Send while busy

The **send-mode setting** (`SettingsStore.SendMode`, "Send message default" in Settings) decides what
a send does while a turn is running:
- **On next tool call** (default): fire `prompt_async` immediately — the server serializes it itself.
  `createUserMessage` stores the prompt at once; the running session loop picks it up at the
  **next agent step** (after the in-flight tool call), not at full idle. Matches the TUI
  (`stream.transport.ts` `runPromptTurn` calls `promptAsync` regardless of busy; its `state.wait`
  gate only prevents a second concurrent UI submit).
- **Queue**: `SessionStore.SendAsync` holds the prompt in the client-side queue
  (`EnqueuePrompt`/`DrainPendingPromptsAsync`, surfaced as the `⏳ N queued` badge) and flushes it
  one at a time when the session goes idle (`OnTurnCompleted` / `ApplySessionStatus`). The queue is
  per-`SessionStore` (so per cached session) and survives session switches; queued prompts drain in
  the background when that session idles.
- **Send immediately**: `SessionStore.SendAsync` interrupts the running turn first
  (`InterruptAsync` → `POST /session/:id/abort`) then fires `prompt_async`, so the new message
  becomes the active request instead of waiting for the next agent step. The abort POST returns once
  the runner is idle, so the following prompt starts a fresh turn. When idle it sends like
  "On next tool call". (Verified sound against the server: `prompt` → `createUserMessage` then
  `loop` → `Runner.ensureRunning`, which starts a fresh run from `Idle`; the TUI/web have no
  interrupt+send flow — this is UnoVibe-only.)

**Busy-state send button:** while a turn runs, the composer's send button becomes a `SplitButton`
(`Controls/SendModeButton.cs`) — the primary click sends with the configured `SendMode`, and the
chevron opens a `MenuFlyout` of the three modes as **one-time overrides** (they never change the
`send.mode` setting; the primary stays the configured default). The menu checkmark + the button
tooltip track the setting live: `ChatPage` keeps the reactive `SendMode` ref synced via
`SettingsStore.Changed` (bounced to the UI thread) and passes it as the component's `Mode` prop;
the component reads `SettingsStore.SendMode` fresh for the primary click. When idle the send button
is a plain button that sends immediately.

### Settings

App settings live in a static `Services/SettingsStore.cs` — one source of truth for every window
(static = shared in-process) and, via a `FileSystemWatcher` on `settings.json`, every process
(reload on external write, debounced + loop-guarded by the last-written content; `Changed` notifies
open settings pages to re-read on the UI thread).

- Persisted to `settings.json` under the app's local-data directory (same place as `recent.json`),
  loaded once at startup (`ConnectPage` ctor, like `RecentConnectionsStore`). Registered in
  `AppJsonContext` (`SettingsFileModel`).
- **Adding a setting** = add a `SettingSpec` to `SettingsStore.Specs` + a `GetValue`/`SetValue` case;
  the data-driven settings page (`Pages/Main/SettingsPage.cs`) renders the row automatically
  (kinds: text / choice / toggle, `Models/SettingsEntry.cs` reactive row model).
- **UI**: a ⚙ **Settings** button in the sidebar bottom status row opens a modal overlay on
  `MainPage` (`SettingsOpen` + `<SettingsPage OnClose=...>`); Back/Done close it. Changes are applied
  live (no Save button) via `SettingsStore.SetValue`.

Settings in use today:
- **Default IDE/Editor** (`editor.command`, default `code`) — `FolderLauncher.OpenInEditor` runs
  `<command> <folder>`; errors surface as a toast.
- **Send message default** (`send.mode`) — see "Send while busy".

### Revert / undo

**API:**
- `POST /session/:id/revert` with `{"messageID":"msg_..."}` (409 when busy — abort first).
- `POST /session/:id/unrevert` with `{}` (400 when no revert).

`revert.messageID` = the user message the conversation is rewound to; the server **keeps** reverted
messages until the next prompt, when `SessionRevert.cleanup` removes messages with
`id >= revert.messageID` (emitting `message.removed`, handled by `ChatStore.ApplyMessageRemoved`)
and clears the marker (a `session.updated` whose info omits `revert`, synced in
`ApplySessionUpsert`).

`ChatStore` holds the reactive `RevertMessageId`/`RevertCountLabel` (+ plain `RevertPromptText`)
and `RevertToMessageAsync(MessageItem)` (abort-if-busy → revert → `ApplyRevertMarker`; restores the
undone prompt (text + re-staged image attachments) into the composer via `RevertPromptText`/
`PendingImages`) plus `UndoLastMessageAsync()`/`RedoLastMessageAsync()` mirroring the TUI.
`UndoLastMessageAsync` currently has **no UI caller** (the chat-box Undo button was removed so users
scroll up to a message instead) but is kept for a planned `/undo` command — delegate new callers to
`RevertToMessageAsync`.

**Per-message revert to a specific message:**
every user message renders a small always-visible **↶ revert icon** in an action row under its text
bubble (`MessageTextPart` (per-text-part bubble + action row) → its `RevertRequested` →
`MessageView.OnPartRevertRequested` re-raises `MessageView.RevertRequested` →
`ChatPage.OnMessageRevertRequested` → `Store.RevertToMessageAsync`), which rewinds the conversation
to that exact user message (web-client per-message revert / TUI dialog-message parity).

Clicking the ↶ opens a **confirmation flyout** (`Button.Flyout` auto-opens on click — Uno calls
`OpenAssociatedFlyout()` in `Button.OnClick`, and `Click` also fires, so the outer button has NO
`@Click` handler; the flyout's "Undo" button performs the real revert and hides the flyout).
The flyout is `Placement=BottomEdgeAlignedRight` (opening it downward avoids covering the message
above being removed) with **no Cancel button** — it's light-dismissed (`ShowMode=Auto`), with an
italic "Click outside to cancel" hint.
The action row is deliberately **not hover-revealed** — toggled Visibility would reflow the message
and fight the stick-to-bottom autoscroll; future actions (fork etc.) go in the same row.

**No message refetch after undo/redo** (the TUI/web don't do one either):
the server keeps reverted messages until the next prompt, so the local `Messages` list is already
authoritative and the revert point is just toggled.
The chat page hides messages with `id >= RevertMessageId` (a per-item conditional in the message
`foreach`, reactive on `RevertMessageId` — NOT a physical removal, so Redo can restore from the
still-cached list) and renders a "N message(s) reverted" card with a Redo button (Redo = revert
forward to the next user message, or `unrevert` when none).
The message `foreach` is **keyed by `m.Id`** so QuickMarkup reuses MessageView blocks across
collection resets instead of recreating every element.
Message ids (`msg_...`) are lexicographically sortable — compare with
`StringComparer.Ordinal.Compare`, never parse.

TUI ref: `packages/tui/src/routes/session/index.tsx` undo/redo + revert-card Match blocks;
web ref: `use-session-commands.tsx` + `pages/session/timeline/model.ts`
(`selectVisibleUserMessages` keeps `id < revertMessageID`).

### Image attachments

The ChatPage attach (camera) button opens `Windows.Storage.Pickers.FileOpenPicker` (XDG portal on
Linux; `FileTypeFilter` `.png/.jpg/.jpeg/.gif/.webp/.bmp`) and stages `ImageAttachment`s into
`ChatStore.PendingImages`, shown as a thumbnail strip (Row 3) with ✕ remove buttons;
`PendingImageCount` drives strip visibility.

On send, `OpencodeClient.SendPromptAsync` builds prompt parts from the text (omitted if
whitespace-only) plus one `{type:"file", mime, filename, url:"data:<mime>;base64,..."}` per pending
image, then clears the strip.
Sent/echoed image parts render as a thumbnail bubble in `MessageView` via
`PartItem.IsImage`/`Image` (`LoadImageAsync` decodes the data URL fire-and-forget; non-image `file`
parts keep the old `file: <name>` line).
Deliberately **no** model `capabilities.attachment` check at attach time (mirrors TUI/web);
guarding image-incapable models is a known follow-up.

### Fork conversation

**API:**
- `POST /session/:id/fork` with body `{}` (full-session fork) or
  `{"messageID":"msg_..."}` (fork at a message) returns the new session's `Session.Info`.
- The server copies every message with `id < messageID` (the forked-at message itself is
  **excluded**; message ids are re-mapped, compaction `tail_start_id`s rewritten) into a new session
  titled `"<original title> (fork #N)"` (`Session.fork` in `session.ts`; fork input
  `{ sessionID, messageID? }`).
- Forked sessions get **no `parentID`** (only `task` subagents do), so they appear as normal root
  sessions in the sidebar with no back button; `session.created` is emitted so
  `ApplySessionUpsert` adds it to `Sessions`.

**UnoVibe UI:**
every user message renders a **⇆ fork icon** (WinUI `Symbol.Switch` glyph) in the action row next
to the ↶ revert icon (`MessageTextPart` → its `ForkRequested` → `MessageView.OnPartForkRequested`
re-raises `MessageView.ForkRequested` → `ChatPage.OnMessageForkRequested` →
`Store.ForkFromMessageAsync`).
Unlike revert there's **no confirmation flyout** (fork is non-destructive — it creates a new session).

`ChatStore.ForkFromMessageAsync(MessageItem)` calls `ForkSessionAsync(_sessionId, message.Id)`, then
`SwitchSessionAsync(forked.Id)` (loads the copied history, clears unread/status), then restores the
forked-at message's prompt into the composer via the plain `ForkPromptText` field (set from
`PromptTextFromMessage`) + `StageImagesFromMessage` for re-staged attachments — the user
edits/continues from there, matching the TUI/web fork-navigate-with-prompt flow.
`ForkPromptText` is reset in `ResetRevertState()` (connect/new/switch/delete).

**Full-session fork** (no message id) is available from a **⇆ button in the ChatPage header row**
(right side, next to the stats button; `Symbol.Switch` glyph, tooltip "Fork full session", disabled
until a session exists) wired to `ChatStore.ForkFullSessionAsync()` — same
`ForkSessionAsync(_sessionId)` → `SwitchSessionAsync(forked.Id)` flow but with no composer restore
(the whole conversation is copied, nothing to re-inject).

`OpencodeClient.ForkSessionAsync` uses the **legacy** `/session/:id/fork` route — the newer
`/api/session/:id/fork` HttpApi route is absent on the current dev server (1.17.18).
Note: the fork-point message itself is excluded from the new session, so the composer prompt is what
re-injects it; `SwitchSessionAsync` falls back to `GetSessionAsync` when the fork isn't yet in the
sidebar list (race with `session.created`).

### Chat autoscroll (stick-to-bottom)

`ChatPage` tracks `_stickToBottom`, updated by `scrollHost.ViewChanged` (every event, incl.
intermediate drag/inertia frames): within 40px of the bottom ⇒ pinned, anything above ⇒ unpinned.

Follow-the-stream scrolling is driven **only** by `messagePanel.SizeChanged` (the scroll content —
fires **after** the frame's layout pass, so `ScrollableHeight` is never stale and the viewport
never jumps to the top on session select). `SizeChanged` is the single trigger: it covers new
messages (collection changes resize the panel), in-place streaming deltas, and toggling a collapsed
Thinking header (which resizes the panel) — so an expanded reasoning block follows while the agent
is thinking without a redundant per-delta scroll.
(A previous `ChatStore.PartContentChanged` event fired on every `ApplyPartDelta`/`ApplyPartUpdated`;
it was removed because each scroll instantly re-pinned via `ChangeView`, killing the user's
in-progress wheel-scroll animation — the collapse/expand case proved it.)

All scroll triggers only run while pinned:
- A manual scroll-up disables autoscroll.
- Scrolling back down to the bottom re-enables it — never before the bottom is hit.
- Explicit app actions (send, continue, undo/redo, permission card) call `ForceScrollToBottom()`
  to re-pin regardless of position.
- A `Reset` on `Messages` (session switch/new session) also re-pins.

Programmatic scrolls use `ChangeView(..., disableAnimation: true)` so they raise exactly one
non-intermediate `ViewChanged` and never falsely unpin.

## Known Working State / Conventions

> **Store split (router + per-session stores):** the feature notes below predate the split and
> name `ChatStore` for everything. Today `ChatStore` is the router (connection, sidebar, shared
> options, permissions, toasts, the `SessionStore` cache, and the `Active` re-point). Anything
> per-session — messages, composer mode/model/variant, usage/context stats, revert/redo, retry
> card, pending images, `SendAsync`/`RenameSessionAsync`/`SetMode`/`SetModel`/`SetVariant` —
> lives on the cached `SessionStore` and is reached via `Store.Active.X` (or `Store.Active.X(...)`
> from `ChatPage` code-behind). `ChatPage` re-hooks the active store's message list on the
> router's `ActiveStoreChanged` event.

> **No-rebuild sidebar model:** sidebar state lives on persistent instances — `SessionInfo`
> items and `DirectoryGroup` groups are reused (never recreated). `RefreshSessionsCoreAsync`
> reconciles `Sessions` in place (drop gone / update survivors via `ApplySessionUpdate` /
> append new), `ReconcileDirectoryGroups` reconciles each group's `Session` list via
> `ReconcileSessionCollection` (Remove/Insert/Move, reference-identity) and reorders groups
> with `ObservableCollection.Move`, and `ReconcileActiveSubagents` does the same for the chat
> page's subagent strip. Per-session sidebar flags (`_sessionFlags`, keyed by session id) stay
> the authoritative store for busy/unread/outcome/attention because SSE can fire for sessions
> not yet in the list (subagent permission races, background outcome before listing).

- A dev `opencode serve` is normally left running on `http://localhost:4196` (manual instance);
  use it as the positional URL argument for day-to-day runs (`UnoVibe http://localhost:4196`).

### ConnectPage

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

### Small-screen layout (MainPage / ChatPage)

On small windows the sidebar and chat can't both fit, so they become **two full-width views**
switched by a flag; wide windows keep the side-by-side layout and ignore the flag.
The single source of truth is `MainPage` (the root page, so it sees the whole window width):
- `MainPage` declares `provide bool IsCompact = false;` and `provide bool IsSidebarView = false;`.
  `OnRootSizeChanged` (a `SizeChanged` handler attached from its `[QuickMarkupConstructor]` Ctor)
  sets `IsCompact` when the width crosses `CompactBreakpoint = 820`, and **resets `IsSidebarView`
  to false** whenever it enters/leaves compact, so a resize starts from the chat view.
- Layout: computed `SidebarColumnWidth`/`ChatColumnWidth` (GridLength) + `SidebarVisibility`/
  `ChatVisibility`. Wide → sidebar 280 + chat star, both visible. Compact → `IsSidebarView` true:
  sidebar full-width star + chat Collapsed/0; false: chat full-width star + sidebar Collapsed/0.
  Both panels stay **mounted** (just Collapsed), so chat scroll/input state survives view switches.
- **Switching views** (all via the shared injected `Reference<bool>`):
  - `ChatHeader` shows a hamburger (`Symbol.GlobalNavButton`, glyph 0xE700, added in
    `SymbolExtemsion.cs`) when compact → `IsSidebarView = true`.
  - `SessionSidebar` shows a "Back to chat" button when compact → `IsSidebarView = false`;
    tapping a session also returns to chat after `SwitchSessionAsync`.
  - `FolderActions.OnNewSession` (group "+") and `SessionSidebar.OpenFolderAndStartSessionAsync`
    return to chat after creating a session.
- The chat sub-components `inject? bool IsCompact;` (optional — defaults to false/desktop when the
  provider is absent) and get the **same** `Reference<bool>` via the provide/inject context chain
  (ChatPage → MainPage), so one resize reflows the whole window:
  - `ChatHeader`: on compact the inline cost/tokens/ctx summary moves to a second header line
    (costs/context stay visible) instead of hiding; shrinks the horizontal padding/spacing.
    The title row is a Grid whose title star-column truncates with an ellipsis
    (`TextTrimming.CharacterEllipsis`) while the pencil/edit button keeps its Auto column.
  - `ChatComposer`: hides the Mode/Model/Variant labels, narrows the mode/variant combos
    (MinWidth 90 → 76) and the `ModelPicker` (MinWidth 200 → 120), and tightens paddings/spacing.
    The picker row stays a horizontal `StackPanel` (Uno's `WrapPanel` here has no `Spacing`/`Padding`).
  - `ChatStatusArea`: shrinks the horizontal padding to match the header/composer.

### Tips about `unovibe` CLI and environment variables

- `unovibe` CLI command is not put in path automatically for them. So, they can't use without
  adding manually. Thus, we commented out those hints for now until we support installing CLI command.

### Sidebar folder actions

Each `SessionSidebar` directory-group header shows, left of the "+" (new session) button, two small
icon buttons — `Symbol.Code` (editor, tooltip "Open folder in editor") and `Symbol.OpenLocal`
(file manager, tooltip "Open folder in file manager").
`SessionSidebar.RunFolderAction` delegates to `Services/FolderLauncher.cs`
(`OpenInEditor`/`OpenInFileManager`), which validates `Directory.Exists` then launches
`<command> <dir>` where the command is the **Default IDE/Editor** setting (`SettingsStore.EditorCommand`,
default `code` — see "Settings") and, for the file manager, `explorer.exe <dir>` on Windows or
`open`/`xdg-open <dir>` on macOS/Linux. Launch failures surface as an error toast via `Store.ShowToast`.
`Symbol.Code` is defined as `(Symbol)0xe943` in `SymbolExtemsion.cs`.

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

### Connection details

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

### Logging

Logging for the app run goes to `/mnt/LinuxProgramData/tmp/opencode/app_run.log`.
Harmless X11 warnings about `_NET_WM_STATE` / `OverlappedPresenter` appear on launch and can be
ignored.

### Markdown rendering

`MarkdownView` in `UnoVibe/Controls/MarkdownView.cs`, powered by the `Markdig` package.

**Default behavior:**
- Assistant text parts render markdown by default.
- User text parts default to plain accent-bubble `TextBlock`s but can be toggled to markdown too
  (see `MessageView.cs` text-part branch).

There is **no RichTextBlock on Uno** (it's a `[Uno.NotImplemented]` stub) and no WCT markdown
component, so `MarkdownView` renders each Markdig block as its own stacked element and inline markup
as `Run`/`LineBreak`/`Hyperlink` in a TextBlock's `Inlines`.

**Contiguous flow blocks (paragraphs + headings) are merged into a single TextBlock joined by
`LineBreak` inlines** so the user can select text across multiple lines/paragraphs at once (each block
would otherwise be a separate TextBlock, breaking cross-block selection); headings keep their
size/weight via per-run `FontSize`/`SemiBold` on the `InlineStyle` record.
Code/quote/list/table/hr stay separate elements (borders/backgrounds need them).

**Streaming model:**
`Text` is the reactive markdown source; on each delta the component re-parses the whole string
(Markdig is fast — ~8.5 GB/s, ~96µs for a typical message) then reconciles the rendered block stack
by content key (`flow:FNV` for a merged-flow span, `BlockKind + FNV` otherwise; spans split on
`Block.Line`), keeping elements with unchanged keys and rebuilding from the first divergent block —
so appending to the tail rebuilds only the last element.
Markdig natively handles unfinished input (open fence stays a code block via
`FencedCodeBlock.ClosingFencedCharCount == 0`; unclosed inline markers stay literal), matching the
web client's streaming "heal".

**PlainMode:**
`PlainMode` (a `bool` reference) switches to a raw-text `TextBlock`; the **toggle UI lives outside
the component** — `MessageTextPart` (in `UnoVibe/Pages/Chat/MessageTextPart.cs`) owns the per-text-part
bubble: it renders the accent/card `Border` around a `MarkdownView` plus a per-part action row
(markdown/plain bullets↔Aa toggle for both roles, and the ↶ undo button for user messages), and keeps
its own internal `PlainMode` (defaulted in its ctor: user → plain, assistant → markdown) so toggling
is scoped to just that bubble.
The bubble + action row align right for user messages and left for assistant messages.
Both roles render text parts through `MarkdownView`; user bubbles keep one consistent look in both
states — a low-alpha accent tint (`new SolidColorBrush(accent.Color with { A = 25 })`, reactive on
`theme.Accent`) + 1px CardStroke, so the bubble is distinguishable from the full-accent hyperlink
color.

**Reasoning blocks** (`ToolViewReasoning`, expanded state) render their summary body through
`MarkdownView` too — markdown by default with the same bullets↔Aa toggle, shown only while expanded.

**Deliberate simplifications for the prototype:**
- HTML blocks render as raw source in a code-style box (content from `HtmlBlock.Lines`, not source
  spans — a span-slicing edge produced empty boxes).
- Tables render as a real Grid (star columns honoring `TableColumnDefinition.Width`, header row
  SubtleFill + SemiBold, per-cell DividerStroke gridlines, column alignment, ColumnSpan/RowSpan via
  `Grid.SetColumnSpan`/`SetRowSpan`, invalid/zero-column tables fall back to raw source).
- **Fenced code blocks get ColorCode syntax highlighting.** `RenderCode` asks
  `UnoVibe/Controls/CodeHighlighter.cs` to colorize the block's text (`ColorCode.Core` —
  a `TextBlockFormatter : CodeColorizerBase` emits styled `Run`s into the code `TextBlock`'s
  `Inlines`, flattened from the scope tree via a `List<Scope>` stack; `EffectiveStyle` returns the
  innermost scope with a style entry, fixing ColorCode's own "previous scope" quirk). The language
  comes from `FencedCodeBlock.Info` (trimmed) via `Languages.FindById`, so alias fences work
  (`ts`→typescript, `csharp`→c#, `sh`→bash, `py`→python); indented code blocks have no `Info`, and
  languages with no ColorCode grammar render as plain text. Theme (dark vs light) is detected once
  per colorize via `UISettings` background brightness (same heuristic as `AccentPalette`) and picks
  a cached `StyleDictionary.DefaultDark`/`DefaultLight`; brushes come from a `BrushFromHex`
  `SolidColorBrush` cache (`ColorCode.Styling.Style` is aliased — `Style` clashes with
  `Microsoft.UI.Xaml.Style`). Code blocks use a fixed `Consolas` font.
- Inline code (`CodeInline`) is tinted with the **secondary accent**
  (`AccentPalette.InlineCodeBrush` — the primary accent hue-rotated −40° into a teal family,
  brightness-shifted toward the theme background: `Light2` in dark themes, `Dark2` in light).
  This keeps `code` visually distinct from accent-colored links. The shared palette service lives
  in `UnoVibe/AccentPalette.cs` (hue shift + WinUI light/dark variants) for reuse.
- `Hyperlink` only for absolute URLs (email autolinks `<a@b.c>` get a `mailto:`-prefixed Uri so
  they navigate).

Reuse in another QuickMarkup project: copy this file + `AppSymbolIcon.cs` + `CodeHighlighter.cs`
and add the Markdig + ColorCode.Core packages.

### Chat input suggestion box

`SuggestBox` in `UnoVibe/Controls/SuggestBox.cs` — a self-contained QuickMarkup component
(multiline TextBox + attached suggestion `Flyout`) that offers `@` and `/` completions.

**Public API:**
`Prefixes` (default `"/@"`) and `Providers` (suggestion sources) are public members; it raises
`SubmitRequested(sender, text)` on bare Enter (flyout closed) and offers `Clear()`; every other
property/event (`Text`, `PlaceholderText`, `MaxHeight`, `PreviewKeyDown`, ...) forwards to the inner
TextBox via `MarkupNode`.

**Focus management:**
The flyout uses `ShowMode=Transient` so it never steals focus on open, plus a `GotFocus` handler on
the flyout content that bounces focus back to the input (mirrors RichSuggestBox's
`SuggestionList_GotFocus`) — the editor keeps focus for the whole suggestion session.

**Parsing + dispatch:**
Parsing + dispatch live in `SuggestionBoxController` (`UnoVibe/Controls/`) — every prefix (both `/`
and `@`) triggers at start-of-token (start of input or preceded by whitespace), so
`foo /skill-name` and `foo @file` work, while `foo/bar`/email `foo@bar` do not; providers route per
trigger char. Items flagged `InputStartOnly` on the `SuggestionItem` (built-in whole-input commands
like `/new`) are filtered out unless the trigger is at position 0, so `/new` only appears when the
input starts with `/`, while skills (`/quickmarkup`) and mentions work anywhere.
Row model in `Controls/SuggestionItem.cs`, providers in `Services/SuggestionProviders.cs`
(`namespace UnoVibe.Controls`).

**Live server data (Phase 2, done):**
- `ServerCommandSuggestionProvider`: legacy `GET /command?directory=` first, falling back to
  `GET /api/command?location[directory]=`; maps MCP entries with a ` :mcp` display suffix,
  `source == "skill"` → skill kind; commands/MCP are `InputStartOnly` so they only show when `/`
  is the first char — TUI parity.
- `ServerSkillSuggestionProvider`: legacy `GET /skill?directory=` first, falling back to
  `GET /api/skill?location[directory]=`; skills are insertable anywhere.
- `ServerFileSuggestionProvider`: `Trigger = '@'`, `GET /api/fs/find` only — the legacy `/fs/find`
  route 404s, verified.

All three take `Func<OpencodeClient?> client` + `Func<string> directory` (wired to `Store.Client` +
`Store.ActiveDirectory()` — the store's `Client` accessor and `ActiveDirectory()` are public).

**No mock fallback** (mock providers were deleted on request): a null client, unreachable server,
or empty response yields an empty list and the flyout closes.

**Route skew — why the legacy routes are primary:**
on the running dev server (opencode **1.17.18**), the legacy `/command?directory=`/
`/skill?directory=` routes return the FULL data (project skills like `quickmarkup`, MCP entries,
user commands, and the `source` field — bare arrays, no wrapper), while the newer `/api/*` HttpApi
surface returns only built-ins (`init`/`review`; built-in skill only) with `source` omitted.
The client therefore tries legacy first and keeps `/api/*` (wrapped `{location, data}`) as a fallback
for servers that drop the legacy routes; `OpencodeClient.FetchItemArrayAsync` accepts both a bare
array and the `data` envelope.
Skills appear from both providers and are deduped by `Key` (`skill:<name>`) in
`SuggestionBoxController`. The `:mcp` suffix is display-only (never inserted), matching the TUI's
`commands` memo (`autocomplete.tsx`).
`OpencodeClient.GetCommandsAsync`/`GetSkillsAsync`/`FindFilesAsync` parse the item arrays; the
deep-object location param is sent as `location%5Bdirectory%5D=<escaped>`.
The current dev server is 1.17.18 — older than the opencode-src checkout (1.18.11) whose
`/api/command` handler folds skills in via `Command.state`, so the `source == "skill"` mapping stays
as a defensive branch.

**No client-side command expansion:**
the server does NOT expand `/name args` at the REST layer — UnoVibe inserts `/name ` text and sends
it through the normal `SendAsync`/`prompt_async` path; the session loop resolves the command
server-side. Never read `Command.Info.template` from the REST list (it can serialize as a Promise
stub); `hints` (from `$1..$n`/`$ARGUMENTS`) exist but are unused.
The legacy route contract (bare arrays, full `source` field) vs the `/api/*` contract
(wrapped `{location, data}`) is documented in the "Known Working State" notes above and in
`OpencodeClient.cs` itself.
