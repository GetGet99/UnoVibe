# Desktop notifications

Reference for the desktop-notifications bridge.
**Read this file when** editing `Helpers/Notifications.cs`, `App.xaml.cs` notification wiring,
or the notification polyfills under `UnoVibe/Polyfills/{Linux,MacOS}/`.
The shared `UnoVibe.Polyfills.MacOS.ObjC` binder used by macOS notifications is also used by the
macOS folder-picker polyfill (see [`polyfills.md`](polyfills.md)).

## In-app toast overlay (a separate system)

Errors and transient notices shown INSIDE the app use the in-app toast surface (`ToastsProvider`:
`ShowError`/`ShowWarning`/`ShowToast`), rendered as a top-right card that auto-dismisses.
Errors must never be written to `ConnectionStatus` (see AGENTS.md banned patterns) — that field
carries only the connect lifecycle, plus connect-time failures shown on the ConnectPage status
line (no toast host exists until the main page mounts).

## OS toast delivery (`Helpers/Notifications.cs`)

`Helpers/Notifications.cs` bridges the chat sidebar indicators to native desktop notifications.
Every public method is a platform-dispatching façade, so callers need no `#if` guards.

- **The WASDK (WinUI), desktop-Linux and desktop-macOS (Skia) targets share ONE toast path**:
  the facade builds the toast through the Windows App SDK's `AppNotificationBuilder` and hands it
  to `AppNotificationManager.Default.Show`. On WinUI those are the real WASDK types; on Linux and
  macOS polyfills provide the same WASDK-named types — Linux sends over the session D-Bus
  `org.freedesktop.Notifications` service, macOS via `terminal-notifier` (when on PATH) falling
  back to `osascript`. Only platform-specific bits (registration, the foreground check) stay
  behind their own `#if`s.
- **Anywhere else:** no-op (desktop-Windows has no notification polyfill yet).

- The app calls `Notifications.Initialize()` in `OnLaunched` (before the first window) and
  registers each window after it activates. The HWND is resolved on demand at check time.
- Fires for the same events the sidebar indicators show: **background completion** and pending
  **question**/**permission** arrivals.
- **Focus gating is per-window:** a toast only fires when it carries info the user isn't already
  looking at. Background-session events always toast; active-session events are suppressed only
  while the owning window is the foreground window. On Linux the foreground check compares by
  **WM_CLASS** (the real X11 window id is not exposed by Uno); on macOS it compares the frontmost
  app's **process id**, so *any* app window being focused suppresses the whole app's
  active-session toasts there.
- Session titles default to "New Chat" for the server's default titles.
- Toast click-activation (switching to the session/replying) is **not** wired.

## Linux notification polyfill (`UnoVibe/Polyfills/Linux/`)

- The polyfill defines the WASDK API shape (manager/builder/notification as plain holders),
  so the facade's shared builder body compiles unchanged on both targets.
  Only the surface the app actually uses is provided, not the full WASDK API.
- **D-Bus send flow:** each toast opens its own session-bus connection and calls
  `org.freedesktop.Notifications` `Notify`, fire-and-forget; failures are logged, never thrown.
- **Foreground gate:** Uno exposes no real X11 window id, so the check reads the EWMH
  `_NET_ACTIVE_WINDOW` root property and matches the active window's `WM_CLASS` against the
  app id (plus fallbacks) — every UnoVibe window shares that class, so "any app window active"
  is the gate. Uses Xlib via `[DllImport("libX11.so.6")]` (ships with every X/XWayland server;
  no new package). A no-op error handler is installed once so Xlib's default exit-on-error
  handler can't kill the app when a window closes mid-check. Failures return "not focused" →
  the toast fires (the conservative default).

## macOS notification polyfill (`UnoVibe/Polyfills/MacOS/`)

- The macOS polyfill supplies the same WASDK API shape. Delivery spawns platform tooling:
- **`terminal-notifier` first, `osascript` fallback.** A clean-exit requirement means a broken
  install falls back. Both are fire-and-forget; failures are logged, never thrown.
- **Why not the native UserNotifications framework?** It hard-requires a signed `.app` bundle
  identity, while UnoVibe runs as a bare binary. A future packaged `.app` could add a native
  path and keep these CLI routes as the unbundled fallback.
- **Attribution caveats:** with `terminal-notifier` the toast carries the tool's own identity;
  the `osascript` route is attributed to **Script Editor** (enable each under System Settings →
  Notifications). The `-sender` flag is deliberately **not** passed.
- **Foreground gate:** macOS exposes no per-window foreground API to an unbundled process, so the
  check compares the frontmost app's **process id** with the current process — any app window
  focused counts as foreground. Failures return "not focused" → the toast fires.