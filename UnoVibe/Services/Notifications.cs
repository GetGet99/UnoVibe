using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using UnoVibe.Models;

namespace UnoVibe.Services;

/// <summary>
/// Bridges the chat sidebar indicators (background completion, pending question/approval) to
/// native desktop notifications. Every public method is a platform-dispatching façade, so callers
/// need no <c>#if</c> guards:
/// - The WASDK (WinUI) and desktop-Linux (Skia) targets share ONE toast path, guarded
///   <c>#if WASDK || DESKTOP_LINUX</c>, that builds the toast through the Windows App SDK's
///   <c>AppNotificationBuilder</c> and hands it to <c>AppNotificationManager.Default.Show</c>.
///   On WinUI those are the real WASDK types; on Linux they are polyfilled
///   (<c>UnoVibe/Polyfills/Linux/AppNotifications.cs</c>), whose manager sends the toast over the
///   session D-Bus <c>org.freedesktop.Notifications</c> service (the interface notify-send uses).
/// - On the WASDK target <c>AppNotificationManager.Register</c> also acquires the app identity,
///   including the COM registration that lets an unpackaged app show toasts.
/// - Anywhere else this is a no-op.
///
/// A toast only fires when it carries information the user isn't already looking at: an event that
/// is visible when its owning window is focused (e.g. a question on the active session, shown
/// inline in chat) is suppressed while that window is the foreground window. The gate is scoped
/// per-window: each event passes the <c>Window</c> whose store raised it, so a second window being
/// focused doesn't suppress another window's active-session toasts. (On Linux the foreground check
/// matches the app's WM_CLASS instead of a per-window handle — see the polyfill's doc — so any app
/// window being focused suppresses the whole app's active-session toasts there.)
/// </summary>
internal static class Notifications
{
#if WASDK
    private static bool _registered;
    // Registered app windows (used for the foreground check's fallback when an event doesn't
    // carry its owning window). Kept as Window instances — the HWND is resolved on demand in
    // IsWindowInForeground, since a live Window is always more reliable than a cached handle.
    private static readonly HashSet<Window> _windows = new();
#endif

    /// <summary>
    /// Registers the app to show app notifications. Called once at startup (App.OnLaunched,
    /// before the first window is created); no-op elsewhere.
    /// </summary>
    public static void Initialize()
    {
#if WASDK
        try
        {
            var manager = AppNotificationManager.Default;
            // Register() requires a NotificationInvoked handler to be attached first; the handler
            // is unused because toasts carry no activation arguments (click-activation to switch
            // to the session is a follow-up, not wired here).
            manager.NotificationInvoked += (_, _) => { };
            manager.Register();
            _registered = true;
        }
        catch (Exception ex)
        {
            // Registration can fail on self-contained/unpackaged setups without a package identity;
            // the notification calls then no-op instead of crashing the app.
            _registered = false;
            System.Diagnostics.Debug.WriteLine($"UnoVibe: app-notification registration failed: {ex.Message}");
        }
#elif DESKTOP_LINUX
        // Nothing to register at startup — each toast is sent lazily over the session D-Bus
        // org.freedesktop.Notifications service. This just installs the no-op Xlib error handler
        // so the foreground check can't trip Xlib's default (exit-)error handler.
        AppNotificationManager.Default.Register();
#endif
    }

    /// <summary>Registers an app window so the foreground check can identify the app.</summary>
    public static void RegisterWindow(Window window)
    {
#if WASDK
        _windows.Add(window);
#endif
    }

    /// <summary>Toast for a session turn that finished (success / error / interrupted).</summary>
    /// <param name="window">
    /// The window owning the store that raised this event. Only its foreground state matters: with
    /// window B focused, window A's active-session events still toast (A isn't visible). When null
    /// the registered-window set is used as a fallback.
    /// </param>
    /// <param name="session">The finished session (falls back to a generic label when null).</param>
    /// <param name="outcome">"" (unknown), "success", "error" or "interrupted".</param>
    /// <param name="visibleWhenFocused">
    /// True when this event is already visible in the chat while the app is focused (the active
    /// session), so the toast is suppressed then — background-session completions always toast.
    /// </param>
    public static void NotifyCompleted(Window? window, SessionInfo? session, string outcome, bool visibleWhenFocused)
    {
        if (!ShouldShow(window, visibleWhenFocused)) return;
        var title = DisplayTitle(session);
        var (heading, body) = outcome switch
        {
            "success" => ("Agent task completed", title),
            "error" => ("Agent reported an error", title),
            "interrupted" => ("Agent turn interrupted", title),
            _ => ("Agent finished", title),
        };
        Show(heading, body);
    }

    /// <summary>Toast for a pending question (<c>question.asked</c>) the user must answer.</summary>
    /// <param name="window">The owning window (see <see cref="NotifyCompleted"/>) — only its
    /// foreground state suppresses the toast.</param>
    /// <param name="session">The asking session (falls back to a generic label when null).</param>
    /// <param name="question">The first question's text, or "" for a generic line.</param>
    /// <param name="visibleWhenFocused">
    /// True when the inline question form is on the active session's chat, so the toast only shows
    /// while the app is not focused; background-session questions always toast.
    /// </param>
    public static void NotifyQuestion(Window? window, SessionInfo? session, string question, bool visibleWhenFocused)
    {
        if (!ShouldShow(window, visibleWhenFocused)) return;
        Show(DisplayTitle(session) + " needs an answer",
            question.Length > 0 ? question : "A question is waiting for your input");
    }

    /// <summary>Toast for a pending permission request (<c>permission.asked</c>).</summary>
    /// <param name="window">The owning window (see <see cref="NotifyCompleted"/>) — only its
    /// foreground state suppresses the toast.</param>
    /// <param name="session">The requesting session (falls back to a generic label when null).</param>
    /// <param name="permissionTitle">Human-readable tool description (e.g. "Edit path/to/file").</param>
    /// <param name="body">Optional detail (e.g. the shell command / diff preview).</param>
    /// <param name="visibleWhenFocused">
    /// True when the approval dialog surfaces in the active session's chat (active session or a
    /// task child of it), so the toast only shows while the app is not focused; background-session
    /// approval requests always toast.
    /// </param>
    public static void NotifyPermission(Window? window, SessionInfo? session, string permissionTitle, string body, bool visibleWhenFocused)
    {
        if (!ShouldShow(window, visibleWhenFocused)) return;
        var detail = permissionTitle.Length > 0 ? permissionTitle
            : body.Length > 0 ? body
            : "An approval request is waiting";
        Show(DisplayTitle(session) + " needs approval", detail);
    }

    /// <summary>True when a toast should be raised for this event (see the class doc).</summary>
    private static bool ShouldShow(Window? window, bool visibleWhenFocused)
    {
#if WASDK
        return _registered && (!visibleWhenFocused || !IsWindowInForeground(window));
#elif DESKTOP_LINUX
        return !visibleWhenFocused || !IsWindowInForeground(window);
#else
        return false;
#endif
    }

    /// <summary>
    /// True when <paramref name="window"/> is the foreground window (falling back to the
    /// registered-window set when no owning window is known). On Windows the HWND is resolved on
    /// demand from the live Window — never cached — so a handle the OS has invalidated can't
    /// linger. On Linux the check matches the app's WM_CLASS instead (see the polyfill's doc).
    /// </summary>
    private static bool IsWindowInForeground(Window? window)
    {
#if WASDK
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        if (window is not null)
        {
            try { return (nint)window.AppWindow.Id.Value == foreground; }
            catch { return false; } // handle not yet created → not "visible", so toast.
        }
        foreach (var w in _windows)
        {
            try
            {
                if ((nint)w.AppWindow.Id.Value == foreground) return true;
            }
            catch { /* window closed mid-iteration; skip it */ }
        }
        return false;
#elif DESKTOP_LINUX
        return AppNotificationManager.Default.IsApplicationInForeground();
#else
        return false;
#endif
    }

    /// <summary>Session display name, mapping the server's default titles to "New Chat".</summary>
    private static string DisplayTitle(SessionInfo? session)
    {
        var title = session?.Title ?? "";
        if (title.Length == 0) return "UnoVibe chat";
        if (title.StartsWith("New session - ") || title.StartsWith("Child session - "))
            return "New Chat";
        return title;
    }

    /// <summary>The one toast path shared by the WASDK and desktop-Linux targets: build the toast
    /// through the Windows App SDK's <c>AppNotificationBuilder</c> and let the platform manager
    /// show it (the real one on WinUI, the D-Bus polyfill on Linux). No-op elsewhere.</summary>
    private static void Show(string heading, string body)
    {
#if WASDK || DESKTOP_LINUX
        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(heading)
                .AddText(TruncateBody(body))
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UnoVibe: toast failed: {ex.Message}");
        }
#endif
    }

    private static string TruncateBody(string body)
    {
        body = body.Replace('\r', ' ').Replace('\n', ' ');
        if (body.Length <= 140) return body;
        return body.Substring(0, 137) + "...";
    }

#if WASDK
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
#endif
}