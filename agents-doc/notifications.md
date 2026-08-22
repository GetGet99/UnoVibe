# Desktop notifications

Reference for the desktop-notifications bridge.
**Read this file when** editing `Services/Notifications.cs`, `App.xaml.cs` notification wiring,
or the notification polyfills under `UnoVibe/Polyfills/{Linux,MacOS}/`.
The shared `UnoVibe.Polyfills.MacOS.ObjC` binder used by macOS notifications is also used by the
macOS folder-picker polyfill (see [`polyfills.md`](polyfills.md)).

## In-app toast overlay (a separate system)

Errors and transient notices shown INSIDE the app do not go through this facade. They use the
in-app toast surface on `ChatStore`: `ShowError(message, title)`, `ShowWarning(message, title)`,
or `ShowToast(new ToastItem { ... })` for full control (variant "info"|"success"|"warning"|"error"
and `DurationMs`; 0/negative = persistent). These set the reactive `CurrentToast`, which `MainPage`
renders as a top-right card (variant-colored accent/background, ✕ button) that auto-dismisses.
Errors must never be written to `ChatStore.ConnectionStatus` (see AGENTS.md banned patterns): that
field carries only the connect lifecycle ("Connecting...", "Connected") plus `ConnectAsync`'s
connect-time failures, which `ConnectPage` shows on its own status line because no toast host
exists until `MainPage` mounts. Producers today: the SSE `tui.toast.show` event, clipboard-copy
confirmations, MCP auth notices, and every migrated error path in `ChatStore`/`SessionStore`.

## OS toast delivery (`Services/Notifications.cs`)

`Services/Notifications.cs` bridges the chat sidebar indicators to native desktop notifications.
Every public method is a platform-dispatching façade, so callers need no `#if` guards.

- **The WASDK (WinUI), desktop-Linux and desktop-macOS (Skia) targets share ONE toast path**,
  guarded `#if WASDK || DESKTOP_LINUX || DESKTOP_MACOS`: the facade's `Show` builds the toast
  through the Windows App SDK's `AppNotificationBuilder` and hands it to
  `AppNotificationManager.Default.Show`. On WinUI those are the real
  `Microsoft.Windows.AppNotifications` types (implemented by Windows/WASDK, **not** by Uno);
  on Linux and macOS the polyfills provide the same WASDK-named types — Linux
  sends the toast over the session D-Bus `org.freedesktop.Notifications` service (the interface
  `notify-send` uses), macOS via `terminal-notifier` (when on PATH) falling back to `osascript`.
  Only platform-specific bits (registration, the foreground check) stay behind their own
  `#if WASDK`/`#elif DESKTOP_LINUX`/`#elif DESKTOP_MACOS`.
- **Anywhere else:** no-op (desktop-Windows has no notification polyfill yet — its folder picker
  is polyfilled, notifications are not).

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
  polyfill's doc below — the real X11 window id is not exposed by Uno); on macOS it compares the
  frontmost app's **process id** via `NSWorkspace.frontmostApplication`, so *any* app window being
  focused suppresses the whole app's active-session toasts there.
- Session titles default to "New Chat" for the server's `"New session - <ISO>"`/`"Child session - <ISO>"`.
- Toast click-activation (switching to the session/replying) is **not** wired; the packaged
  `Package.appxmanifest` does not declare a `windows.toastNotificationActivation` activator, which
  is optional for showing toasts.

## Linux notification polyfill (`UnoVibe/Polyfills/Linux/`)

- **`AppNotifications.cs` supplies the WASDK API shape on Linux.** It defines
  `Microsoft.Windows.AppNotifications.AppNotificationManager`/`AppNotification` and
  `Microsoft.Windows.AppNotifications.Builder.AppNotificationBuilder` (block namespaces — two file-scoped
  namespaces in one file do not compile), so the facade's shared `#if WASDK || DESKTOP_LINUX`
  `AppNotificationBuilder` body compiles unchanged on both targets.
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

## macOS notification polyfill (`UnoVibe/Polyfills/MacOS/`)

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