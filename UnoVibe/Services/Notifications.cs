using UnoVibe.Models;

namespace UnoVibe.Services;

/// <summary>
/// Bridges the chat sidebar indicators (background completion, pending question/approval) to
/// native Windows toast notifications on the WASDK (WinUI) target. Every public method is a
/// no-op on other targets (Skia), so callers need no <c>#if</c> guards — the Windows API code
/// lives behind <c>#if WASDK</c>.
///
/// Uses the Windows App SDK's <c>Microsoft.Windows.AppNotifications</c> API (implemented by
/// Windows/WASDK — not by Uno), whose <see cref="Microsoft.Windows.AppNotifications.AppNotificationManager.Register"/>
/// acquires the app identity itself, including the COM registration that lets an unpackaged app
/// show toasts. A toast only fires when it carries information the user isn't already looking at:
/// an event that is visible when its owning window is focused (e.g. a question on the active
/// session, shown inline in chat) is suppressed while that window is the foreground window. The
/// gate is scoped per-window: each event passes the <c>Window</c> whose store raised it, so a
/// second window being focused doesn't suppress another window's active-session toasts.
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
    /// Registers the app to show app notifications. Called once at startup on the WASDK target
    /// (App.OnLaunched, before the first window is created); no-op elsewhere.
    /// </summary>
    public static void Initialize()
    {
#if WASDK
        try
        {
            var manager = Microsoft.Windows.AppNotifications.AppNotificationManager.Default;
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
#if WASDK
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
#endif
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
#if WASDK
        if (!ShouldShow(window, visibleWhenFocused)) return;
        Show(DisplayTitle(session) + " needs an answer",
            question.Length > 0 ? question : "A question is waiting for your input");
#endif
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
#if WASDK
        if (!ShouldShow(window, visibleWhenFocused)) return;
        var detail = permissionTitle.Length > 0 ? permissionTitle
            : body.Length > 0 ? body
            : "An approval request is waiting";
        Show(DisplayTitle(session) + " needs approval", detail);
#endif
    }

#if WASDK
    /// <summary>True when a toast should be raised for this event (see the class doc).</summary>
    private static bool ShouldShow(Window? window, bool visibleWhenFocused) =>
        _registered && (!visibleWhenFocused || !IsWindowInForeground(window));

    /// <summary>
    /// True when <paramref name="window"/> is the foreground window (falling back to the
    /// registered-window set when no owning window is known). The HWND is resolved on demand
    /// from the live Window — never cached — so a handle the OS has invalidated can't linger.
    /// </summary>
    private static bool IsWindowInForeground(Window? window)
    {
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

    private static void Show(string heading, string body)
    {
        try
        {
            var notification = new Microsoft.Windows.AppNotifications.Builder.AppNotificationBuilder()
                .AddText(heading)
                .AddText(TruncateBody(body))
                .BuildNotification();
            Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UnoVibe: toast failed: {ex.Message}");
        }
    }

    private static string TruncateBody(string body)
    {
        body = body.Replace('\r', ' ').Replace('\n', ' ');
        if (body.Length <= 140) return body;
        return body.Substring(0, 137) + "...";
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
#endif
}