using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace UnoVibe.Helpers;

internal static class NotificationsHelper
{
#if WASDK
    private static bool _registered;
    private static readonly HashSet<Window> _windows = new();
#endif

    public static void Initialize()
    {
#if WASDK
        try
        {
            var manager = AppNotificationManager.Default;
            manager.NotificationInvoked += (_, _) => { };
            manager.Register();
            _registered = true;
        }
        catch (Exception ex)
        {
            _registered = false;
            System.Diagnostics.Debug.WriteLine($"UnoVibe: app-notification registration failed: {ex.Message}");
        }
#elif DESKTOP_LINUX || DESKTOP_MACOS
        AppNotificationManager.Default.Register();
#endif
    }

    public static void RegisterWindow(Window window)
    {
#if WASDK
        _windows.Add(window);
#endif
    }

    public static void NotifyCompleted(Window? window, SessionHead? session, ChatOutcome outcome, bool visibleWhenFocused)
    {
        if (!ShouldShow(window, visibleWhenFocused)) return;
        var title = DisplayTitle(session);
        var (heading, body) = outcome switch
        {
            ChatOutcome.Success => ("Agent task completed", title),
            ChatOutcome.Error => ("Agent reported an error", title),
            ChatOutcome.Interrupted => ("Agent turn interrupted", title),
            _ => ("Agent finished", title),
        };
        Show(heading, body);
    }

    public static void NotifyQuestion(Window? window, SessionHead? session, string question, bool visibleWhenFocused)
    {
        if (!ShouldShow(window, visibleWhenFocused)) return;
        Show(DisplayTitle(session) + " needs an answer",
            question.Length > 0 ? question : "A question is waiting for your input");
    }

    public static void NotifyPermission(Window? window, SessionHead? session, string permissionTitle, string body, bool visibleWhenFocused)
    {
        if (!ShouldShow(window, visibleWhenFocused)) return;
        var detail = permissionTitle.Length > 0 ? permissionTitle
            : body.Length > 0 ? body
            : "An approval request is waiting";
        Show(DisplayTitle(session) + " needs approval", detail);
    }

    private static bool ShouldShow(Window? window, bool visibleWhenFocused)
    {
#if WASDK
        return _registered && (!visibleWhenFocused || !IsWindowInForeground(window));
#elif DESKTOP_LINUX || DESKTOP_MACOS
        return !visibleWhenFocused || !IsWindowInForeground(window);
#else
        return false;
#endif
    }

    private static bool IsWindowInForeground(Window? window)
    {
#if WASDK
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        if (window is not null)
        {
            try { return (nint)window.AppWindow.Id.Value == foreground; }
            catch { return false; }
        }
        foreach (var w in _windows)
        {
            try
            {
                if ((nint)w.AppWindow.Id.Value == foreground) return true;
            }
            catch {   }
        }
        return false;
#elif DESKTOP_LINUX || DESKTOP_MACOS
        return AppNotificationManager.Default.IsApplicationInForeground();
#else
        return false;
#endif
    }

    private static string DisplayTitle(SessionHead? session)
    {
        var title = session?.Title ?? "";
        if (title.Length == 0) return "UnoVibe chat";
        if (title.StartsWith("New session - ") || title.StartsWith("Child session - "))
            return "New Chat";
        return title;
    }

    private static void Show(string heading, string body)
    {
#if WASDK || DESKTOP_LINUX || DESKTOP_MACOS
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
        return string.Concat(body.AsSpan(0, 137), "...");
    }

#if WASDK
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
#endif
}