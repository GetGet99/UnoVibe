#if DESKTOP_LINUX
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Tmds.DBus.Protocol;
using UnoVibe.Polyfills.Linux.DBus;
using Windows.ApplicationModel;

// Supplies the Windows App SDK's toast-notification API surface
// (Microsoft.Windows.AppNotifications.*) on the desktop-Linux target, so the shared code in
// Services/Notifications.cs builds notifications through the SAME WASDK-shaped AppNotificationBuilder
// path as the WASDK (WinUI) target — one guard, `#if WASDK || DESKTOP_LINUX`, one identical body.
// On WinUI these types are the real Windows App SDK API; here they are polyfilled, and the D-Bus
// toast engine + X11 foreground gate live directly on AppNotificationManager:
//   - Register() installs a no-op Xlib error handler (Startup; mirrors WASDK's Register()).
//   - Default.Show(...) sends the toast over the session D-Bus org.freedesktop.Notifications
//     service (fire-and-forget; failures are logged, never thrown).
//   - IsApplicationInForeground() drives the focus gate (see its doc).
// The builder/notification are plain data holders (WASDK builds an XML payload under the hood; the
// polyfill just carries the text lines the manager forwards). Only the surface the app actually
// uses is provided (Builder + Default + Show + Register) — not the full WASDK API (tags, button
// actions, toast activation arguments, ...).

namespace Microsoft.Windows.AppNotifications
{
    /// <summary>A toast notification, polyfill of
    /// <c>Microsoft.Windows.AppNotifications.AppNotification</c>. Produced by
    /// <see cref="Microsoft.Windows.AppNotifications.Builder.AppNotificationBuilder.BuildNotification"/>
    /// and consumed by <see cref="AppNotificationManager.Show"/>. Carries only the text lines added
    /// to the builder; the WASDK-XML payload is an implementation detail this polyfill doesn't need.</summary>
    public sealed class AppNotification
    {
        internal AppNotification(List<string> texts) => _texts = texts;

        private readonly List<string> _texts;

        /// <summary>The text lines in build order (OS renders the first as the heading and the rest
        /// as body lines, matching WASDK's visual layout).</summary>
        internal IReadOnlyList<string> Texts => _texts;
    }

    /// <summary>
    /// Polifill of <c>Microsoft.Windows.AppNotifications.AppNotificationManager</c> for the
    /// desktop-Linux target, carrying the whole desktop-toast implementation. Each toast is a
    /// native desktop notification sent over the session D-Bus to the
    /// <c>org.freedesktop.Notifications</c> service (the same interface <c>notify-send</c> uses,
    /// implemented by every common notification daemon — GNOME, KDE Plasma, XFCE, ...). One shot per
    /// event; a new D-Bus connection is opened per toast so there is no state to leak or sync.
    /// </summary>
    public sealed class AppNotificationManager
    {
        private const string NotificationService = "org.freedesktop.Notifications";
        private static readonly ObjectPath NotificationPath = new("/org/freedesktop/Notifications");

        // All UnoVibe windows carry this WM_CLASS (X11XamlRootHost.SetWMClass uses
        // Package.Current.Id.Name on the Skia X11 host), so matching it identifies our app.
        private static string[]? _classCandidates;

        private AppNotificationManager()
        {
        }

        /// <summary>App-wide notification manager, mirroring <c>AppNotificationManager.Default</c>.</summary>
        public static AppNotificationManager Default { get; } = new();

        /// <summary>Mirrors <c>AppNotificationManager.Register()</c>. The Skia manager needs no app
        /// identity/COM registration — this installs a no-op Xlib error handler so a window closing
        /// mid-check can't hit Xlib's default (exit-)error handler, if it isn't already. D-Bus
        /// availability is probed lazily on each show.</summary>
        public bool Register()
        {
            EnsureNoOpXErrorHandler();
            return true;
        }

        /// <summary>Shows a toast, forwarding its text lines to the D-Bus
        /// <c>org.freedesktop.Notifications</c> service (fire-and-forget; failures are logged, never
        /// thrown).</summary>
        public void Show(AppNotification notification)
        {
            var texts = notification.Texts;
            var summary = texts.Count > 0 ? texts[0] : string.Empty;
            var body = texts.Count > 1 ? texts[1] : string.Empty;
            _ = ShowAsync(summary, body);
        }

        /// <summary>
        /// True when any UnoVibe window is the X11 active (focused) window, per the EWMH
        /// <c>_NET_ACTIVE_WINDOW</c> root property matched against our WM_CLASS. Never throws; any
        /// failure (no DISPLAY, non-EWMH WM, property races) is treated as "not focused" so the toast
        /// fires rather than being swallowed — the same conservative default the WASDK path uses.
        /// Opens a private X connection per call — Xlib handles are not thread-safe and toasts are
        /// rare enough that the ~ms cost is irrelevant.
        /// </summary>
        internal bool IsApplicationInForeground()
        {
            EnsureNoOpXErrorHandler();

            var display = XOpenDisplay(null);
            if (display == IntPtr.Zero) return false;
            try
            {
                var root = XDefaultRootWindow(display);
                if (root == IntPtr.Zero) return false;
                var activeWindow = QueryActiveWindow(display, root);
                if (activeWindow == IntPtr.Zero) return false;
                return HasMatchingWindowClass(display, activeWindow, depth: 0);
            }
            finally
            {
                XCloseDisplay(display);
            }
        }

        private static async Task ShowAsync(string summary, string body)
        {
            try
            {
                var sessionAddress = DBusAddress.Session;
                if (sessionAddress is null) return;

                using var connection = new DBusConnection(sessionAddress);
                await connection.ConnectAsync().ConfigureAwait(false);

                var service = new DBusService(connection, NotificationService);
                // The generated D-Bus client is also named "Notifications" (namespace
                // UnoVibe.Polyfills.Linux.DBus); `var` sidesteps the name.
                var notifications = service.CreateNotifications(NotificationPath);
                await notifications.NotifyAsync(
                    appName: "UnoVibe",
                    replacesId: 0,
                    appIcon: string.Empty,
                    summary: summary,
                    body: body,
                    actions: Array.Empty<string>(),
                    hints: new Dictionary<string, VariantValue>(),
                    expireTimeout: -1).ConfigureAwait(false); // -1 = server default timeout
            }
            catch (Exception ex)
            {
                // No session bus / no notification daemon (ServiceUnknown) / portal hiccup — a toast
                // must never crash the app; the store surfaces these events in the UI anyway.
                System.Diagnostics.Debug.WriteLine($"UnoVibe: toast failed: {ex.Message}");
            }
        }

        /// <summary>Reads the <c>_NET_ACTIVE_WINDOW</c> root property (EWMH); IntPtr.Zero when unset.</summary>
        private static IntPtr QueryActiveWindow(IntPtr display, IntPtr root)
        {
            var atom = XInternAtom(display, "_NET_ACTIVE_WINDOW", true);
            if (atom == IntPtr.Zero) return IntPtr.Zero; // non-EWMH window manager

            if (XGetWindowProperty(display, root, atom, 0, 1, false, IntPtr.Zero,
                    out _, out var format, out var nitems, out _, out var prop) != 0 || prop == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            try
            {
                if (nitems == 0 || format != 32) return IntPtr.Zero;
                // Format-32 values are stored as 'long's (8 bytes on 64-bit, value in the low dword).
                var value = (ulong)Marshal.ReadIntPtr(prop, 0) & 0xFFFFFFFF;
                // None (0) or AllWindows (~0) means "no active window".
                return value is 0 or 0xFFFFFFFF ? IntPtr.Zero : (IntPtr)value;
            }
            finally
            {
                XFree(prop);
            }
        }

        /// <summary>True when the window (or, for reparenting WMs, one of its children) has our
        /// WM_CLASS. The EWMH active window is the client window on modern WMs, but the walk keeps the
        /// check working on older WMs that activate the frame instead.</summary>
        private static bool HasMatchingWindowClass(IntPtr display, IntPtr window, int depth)
        {
            if (depth > 2 || window == IntPtr.Zero) return false;
            if (WindowHasMatchingClass(display, window)) return true;

            if (XQueryTree(display, window, out _, out _, out var children, out var count) != 0
                && children != IntPtr.Zero && count > 0)
            {
                try
                {
                    for (var i = 0; i < count; i++)
                    {
                        var child = Marshal.ReadIntPtr(children, i * IntPtr.Size);
                        if (HasMatchingWindowClass(display, child, depth + 1)) return true;
                    }
                }
                finally
                {
                    XFree(children);
                }
            }
            return false;
        }

        private static bool WindowHasMatchingClass(IntPtr display, IntPtr window)
        {
            var atom = XInternAtom(display, "WM_CLASS", true);
            if (atom == IntPtr.Zero) return false;

            if (XGetWindowProperty(display, window, atom, 0, 64, false, IntPtr.Zero,
                    out _, out _, out var nitems, out _, out var prop) != 0 || prop == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                if (nitems == 0) return false;
                // WM_CLASS is XA_STRING: two NUL-terminated strings — res_name, then res_class.
                var resName = Marshal.PtrToStringAnsi(prop);
                if (resName is not null && MatchesAnyClass(resName)) return true;
                var resClass = Marshal.PtrToStringAnsi(IntPtr.Add(prop, resName?.Length + 1 ?? 0));
                return resClass is not null && MatchesAnyClass(resClass);
            }
            finally
            {
                XFree(prop);
            }
        }

        private static bool MatchesAnyClass(string name)
        {
            foreach (var candidate in ClassCandidates)
            {
                if (string.Equals(candidate, name, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static string[] ClassCandidates
        {
            get
            {
                if (_classCandidates is null)
                {
                    var candidates = new HashSet<string>(StringComparer.Ordinal);
                    try
                    {
                        var packageName = Package.Current.Id.Name;
                        if (packageName.Length > 0) candidates.Add(packageName);
                    }
                    catch { /* Package.CommonInitializations not yet run — fall back below */ }
                    try
                    {
                        var assemblyName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
                        if (!string.IsNullOrEmpty(assemblyName)) candidates.Add(assemblyName);
                    }
                    catch { /* ignore */ }
                    candidates.Add("UnoVibe");
                    _classCandidates = candidates.ToArray();
                }
                return _classCandidates;
            }
        }

        // --- Xlib interop (libX11 ships with every X server / XWayland) ---

        private static object _setupLock = new();
        private static bool _errorHandlerInstalled;
        private static XErrorProc? _errorProc;

        /// <summary>Replaces Xlib's default error handler (which prints and calls exit()) with a
        /// no-op one, so a window destroyed mid-check surfaces as a XGetWindowProperty failure
        /// instead of killing the app. Installed once; the static delegate ref keeps the callback
        /// alive.</summary>
        private static void EnsureNoOpXErrorHandler()
        {
            if (_errorHandlerInstalled) return;
            lock (_setupLock)
            {
                if (_errorHandlerInstalled) return;
                _errorProc = HandleXError;
                XSetErrorHandler(_errorProc);
                _errorHandlerInstalled = true;
            }
        }

        private static int HandleXError(IntPtr display, IntPtr errorEvent) => 0;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int XErrorProc(IntPtr display, IntPtr errorEvent);

        [DllImport("libX11.so.6")]
        private static extern IntPtr XSetErrorHandler(XErrorProc handler);

        [DllImport("libX11.so.6")]
        private static extern IntPtr XOpenDisplay(string? displayName);

        [DllImport("libX11.so.6")]
        private static extern int XCloseDisplay(IntPtr display);

        [DllImport("libX11.so.6")]
        private static extern IntPtr XDefaultRootWindow(IntPtr display);

        [DllImport("libX11.so.6")]
        private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);

        [DllImport("libX11.so.6")]
        private static extern int XGetWindowProperty(
            IntPtr display, IntPtr w, IntPtr property, long longOffset, long longLength, bool delete,
            IntPtr reqType, out IntPtr actualTypeReturn, out int actualFormatReturn,
            out ulong nitemsReturn, out ulong bytesAfterReturn, out IntPtr propReturn);

        [DllImport("libX11.so.6")]
        private static extern int XQueryTree(
            IntPtr display, IntPtr w,
            out IntPtr rootReturn, out IntPtr parentReturn, out IntPtr childrenReturn, out uint nchildrenReturn);

        [DllImport("libX11.so.6")]
        private static extern int XFree(IntPtr data);
    }
}

namespace Microsoft.Windows.AppNotifications.Builder
{
    /// <summary>Polifill of <c>Microsoft.Windows.AppNotifications.Builder.AppNotificationBuilder</c>
    /// — fluently collects the toast's text lines and produces an <see cref="AppNotification"/>.
    /// WASDK renders AddText lines as heading then body; the polyfill keeps that order (first line =
    /// heading, rest = body) so the desktop looks like the WinUI toast.</summary>
    public sealed class AppNotificationBuilder
    {
        private readonly List<string> _texts = new();

        /// <summary>Appends a text line; the first line is the heading, subsequent lines the body.</summary>
        public AppNotificationBuilder AddText(string text)
        {
            _texts.Add(text);
            return this;
        }

        /// <summary>Builds the <see cref="AppNotification"/> carrying the added text lines.</summary>
        public AppNotification BuildNotification() => new(_texts);
    }
}
#endif