# Desktop notifications

Reference for the desktop-notifications bridge.
**Read this file when** editing notification wiring or the notification polyfills.
The shared macOS ObjC binder is also used by the macOS folder-picker polyfill
(see [`polyfills.md`](polyfills.md)).

## In-app toast overlay (a separate system)

Errors and transient notices shown INSIDE the app use the in-app toast surface, rendered as
a top-right card that auto-dismisses.
Errors must never be written to `ConnectionStatus` (see AGENTS.md banned patterns) — that field
carries only the connect lifecycle, plus connect-time failures shown on the ConnectPage status
line (no toast host exists until the main page mounts).

## OS toast delivery

`Helpers/Notifications.cs` bridges the chat sidebar indicators to native desktop notifications.
Every public method is a platform-dispatching façade, so callers need no `#if` guards.

- **The WASDK (WinUI), desktop-Linux and desktop-macOS (Skia) targets share ONE toast path**:
  the facade builds the toast through the Windows App SDK's `AppNotificationBuilder` and hands it
  to `AppNotificationManager.Default.Show`. On WinUI those are the real WASDK types; on Linux and
  macOS polyfills provide the same WASDK-named types. Only platform-specific bits (registration,
  the foreground check) stay behind their own `#if`s. Anywhere else is a no-op (desktop-Windows
  has no notification polyfill yet).
- Fires for the same events the sidebar indicators show: **background completion** and pending
  **question**/**permission** arrivals.
- **Focus gating is per-window:** a toast only fires when it carries info the user isn't already
  looking at. Background-session events always toast; active-session events are suppressed only
  while the owning window is foreground. Failures return "not focused" → the toast fires
  (the conservative default).
- Session titles default to "New Chat" for the server's default titles.
- Toast click-activation (switching to the session/replying) is **not** wired.

## Platform delivery decisions

- Linux sends over the session D-Bus `org.freedesktop.Notifications` service, fire-and-forget;
  failures are logged, never thrown. Uno exposes no real X11 window id, so the foreground check
  matches the active window's `WM_CLASS` against the app id — every UnoVibe window shares that
  class, so "any app window active" is the gate. A no-op Xlib error handler is installed once so
  Xlib's default exit-on-error handler can't kill the app when a window closes mid-check.
- macOS tries `terminal-notifier` first, `osascript` fallback (both fire-and-forget; failures are
  logged, never thrown). The native UserNotifications framework is deliberately **not** used: it
  hard-requires a signed `.app` bundle identity, while UnoVibe runs as a bare binary. The
  `osascript` route is attributed to **Script Editor**, and the `-sender` flag is deliberately
  **not** passed.
