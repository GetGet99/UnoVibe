#if DESKTOP_LINUX
using System.Runtime.InteropServices;
using Tmds.DBus.Protocol;
using UnoVibe.Polyfills.Linux.DBus;

namespace Microsoft.Windows.AppNotifications
{
    sealed class AppNotification
    {
        internal AppNotification(List<string> texts) => _texts = texts;

        private readonly List<string> _texts;

        internal IReadOnlyList<string> Texts => _texts;
    }

    sealed class AppNotificationManager
    {
        private const string NotificationProvider = "org.freedesktop.Notifications";
        private static readonly ObjectPath NotificationPath = new("/org/freedesktop/Notifications");

        private static string[]? _classCandidates;

        private AppNotificationManager()
        {
        }

        public static AppNotificationManager Default { get; } = new();

        public bool Register()
        {
            EnsureNoOpXErrorHandler();
            return true;
        }

        public void Show(AppNotification notification)
        {
            var texts = notification.Texts;
            var summary = texts.Count > 0 ? texts[0] : string.Empty;
            var body = texts.Count > 1 ? texts[1] : string.Empty;
            _ = ShowAsync(summary, body);
        }

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

                var service = new DBusService(connection, NotificationProvider);
                var notifications = service.CreateNotifications(NotificationPath);
                await notifications.NotifyAsync(
                    appName: "UnoVibe",
                    replacesId: 0,
                    appIcon: string.Empty,
                    summary: summary,
                    body: body,
                    actions: Array.Empty<string>(),
                    hints: new Dictionary<string, VariantValue>(),
                    expireTimeout: -1).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UnoVibe: toast failed: {ex.Message}");
            }
        }

        private static IntPtr QueryActiveWindow(IntPtr display, IntPtr root)
        {
            var atom = XInternAtom(display, "_NET_ACTIVE_WINDOW", true);
            if (atom == IntPtr.Zero) return IntPtr.Zero;

            if (XGetWindowProperty(display, root, atom, 0, 1, false, IntPtr.Zero,
                    out _, out var format, out var nitems, out _, out var prop) != 0 || prop == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            try
            {
                if (nitems == 0 || format != 32) return IntPtr.Zero;
                var value = (ulong)Marshal.ReadIntPtr(prop, 0) & 0xFFFFFFFF;
                return value is 0 or 0xFFFFFFFF ? IntPtr.Zero : (IntPtr)value;
            }
            finally
            {
                XFree(prop);
            }
        }

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
                    catch {   }
                    try
                    {
                        var assemblyName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
                        if (!string.IsNullOrEmpty(assemblyName)) candidates.Add(assemblyName);
                    }
                    catch {   }
                    candidates.Add("UnoVibe");
                    _classCandidates = candidates.ToArray();
                }
                return _classCandidates;
            }
        }

        private static object _setupLock = new();
        private static bool _errorHandlerInstalled;
        private static XErrorProc? _errorProc;

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
    sealed class AppNotificationBuilder
    {
        private readonly List<string> _texts = new();

        public AppNotificationBuilder AddText(string text)
        {
            _texts.Add(text);
            return this;
        }

        public AppNotification BuildNotification() => new(_texts);
    }
}
#endif