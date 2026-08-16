#if DESKTOP_MACOS
using System.Diagnostics;
using UnoVibe.Polyfills.MacOS;

// Supplies the Windows App SDK's toast-notification API surface
// (Microsoft.Windows.AppNotifications.*) on the desktop-macOS target, so the shared code in
// Services/Notifications.cs builds notifications through the SAME WASDK-shaped AppNotificationBuilder
// path as the WASDK (WinUI) and desktop-Linux targets — one guard,
// `#if WASDK || DESKTOP_LINUX || DESKTOP_MACOS`, one identical body.
// The macOS toast engine lives directly on AppNotificationManager:
//   - Register() is a no-op (the macOS toasts need no identity/registration, unlike WASDK).
//   - Default.Show(...) posts the toast through the user's tooling: the `terminal-notifier`
//     command when it is on PATH (proper notification identity — the app it ships as), else the
//     classic `osascript -e 'display notification ...'` Standard Addition (attributed to Script
//     Editor — the same fallback opencode and Claude Code use). Failures are logged, never thrown.
//   - IsApplicationInForeground() drives the focus gate (see its doc).
// The builder/notification are plain data holders, exactly like the Linux polyfill. Only the
// surface the app actually uses is provided (Builder + Default + Show + Register).
//
// Why not the native UserNotifications framework? UNUserNotificationCenter hard-asserts with
// "bundleProxyForCurrentProcess is nil" for a process that isn't part of a signed .app bundle,
// and the deprecated NSUserNotificationCenter silently needs a valid main-bundle identifier —
// while UnoVibe runs as a bare binary (`dotnet run` / plain `-r osx-arm64` publish), so only
// the command-line route works today. A future packaged .app could add a UNUserNotificationCenter
// path and keep these command-line routes as the unbundled fallback.

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
    /// desktop-macOS target, carrying the whole desktop-toast implementation. Each toast prefers the
    /// <c>terminal-notifier</c> CLI when it's on PATH (the user's tooling — it gives notifications a
    /// real identity instead of Script Editor's), and falls back to the AppleScript Standard Addition
    /// <c>display notification</c> via <c>osascript</c> (always works on an unbundled process, at the
    /// cost of being attributed to Script Editor and needing Script Editor enabled in
    /// System Settings → Notifications on modern macOS). One shot per event; failures are logged.
    /// </summary>
    public sealed class AppNotificationManager
    {
        private const string OsascriptPath = "/usr/bin/osascript";

        // Resolved once (cached including the negative result via string.Empty).
        private static string? _terminalNotifierPath;

        private AppNotificationManager()
        {
        }

        /// <summary>App-wide notification manager, mirroring <c>AppNotificationManager.Default</c>.</summary>
        public static AppNotificationManager Default { get; } = new();

        /// <summary>Mirrors <c>AppNotificationManager.Register()</c>. macOS toasts need no
        /// registration or app identity — the delivery route is resolved per toast.</summary>
        public bool Register() => true;

        /// <summary>Shows a toast via <c>terminal-notifier</c> (when available) or
        /// <c>osascript</c> (fallback). Fire-and-forget; failures are logged, never thrown.</summary>
        public void Show(AppNotification notification)
        {
            var texts = notification.Texts;
            var summary = texts.Count > 0 ? texts[0] : string.Empty;
            var body = texts.Count > 1 ? texts[1] : string.Empty;
            _ = SendAsync(summary, body);
        }

        /// <summary>
        /// True when any UnoVibe window is the macOS frontmost application. macOS has no per-window
        /// foreground API reachable from an unbundled process, so this mirrors the Linux "any window
        /// focused" semantics by comparing the frontmost app's process id
        /// (<c>NSWorkspace.frontmostApplication</c>) with our own. Never throws; any failure
        /// (no appkit, no frontmost app) is treated as "not focused" so the toast fires rather than
        /// being swallowed — the same conservative default the WASDK path uses.
        /// </summary>
        internal bool IsApplicationInForeground()
        {
            try
            {
                var sharedWorkspace = ObjC.msgSend(ObjC.Class("NSWorkspace"), ObjC.Selector("sharedWorkspace"));
                if (sharedWorkspace == IntPtr.Zero) return false;
                var frontmost = ObjC.msgSend(sharedWorkspace, ObjC.Selector("frontmostApplication"));
                if (frontmost == IntPtr.Zero) return false;
                return ObjC.msgSendLong(frontmost, ObjC.Selector("processIdentifier")) == Environment.ProcessId;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Sends one toast: <c>terminal-notifier</c> first, <c>osascript</c> on any failure.</summary>
        private static async Task SendAsync(string summary, string body)
        {
            try
            {
                var notifier = FindTerminalNotifier();
                if (notifier is not null
                    && await TryRunTerminalNotifierAsync(notifier, summary, body).ConfigureAwait(false))
                {
                    return;
                }
                await RunOsascriptAsync(summary, body).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A toast must never crash the app; the store surfaces these events in the UI anyway.
                System.Diagnostics.Debug.WriteLine($"UnoVibe: toast failed: {ex.Message}");
            }
        }

        /// <summary>Searches PATH for the <c>terminal-notifier</c> executable (brew and the Ruby gem
        /// both put a launcher on PATH). Cached, including the negative result.</summary>
        private static string? FindTerminalNotifier()
        {
            if (_terminalNotifierPath is not null) return _terminalNotifierPath;
            var path = Environment.GetEnvironmentVariable("PATH");
            if (path is not null)
            {
                foreach (var dir in path.Split(Path.PathSeparator))
                {
                    var candidate = Path.Combine(dir, "terminal-notifier");
                    if (File.Exists(candidate))
                    {
                        _terminalNotifierPath = candidate;
                        return candidate;
                    }
                }
            }
            _terminalNotifierPath = string.Empty; // cache the negative result
            return null;
        }

        /// <summary>Posts the toast via <c>terminal-notifier</c>. Waits up to 10s (a hung invocation
        /// is killed) and reports success only on a clean exit, so a broken installation falls back.</summary>
        private static async Task<bool> TryRunTerminalNotifierAsync(string path, string summary, string body)
        {
            try
            {
                var psi = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true };
                psi.ArgumentList.Add("-title");
                psi.ArgumentList.Add(summary);
                psi.ArgumentList.Add("-message");
                psi.ArgumentList.Add(body);
                using var proc = Process.Start(psi);
                if (proc is null) return false;
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                    return false;
                }
                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Posts the toast via <c>osascript -e 'display notification ...'</c> (
        /// fire-and-forget; failures are logged, never thrown).</summary>
        private static async Task RunOsascriptAsync(string summary, string body)
        {
            try
            {
                // AppleScript one-liner: body/title are double-quoted literals. Arguments go through
                // ArgumentList (no shell involved), so only the AppleScript string escapes matter.
                var script =
                    $"display notification \"{AppleScriptEscape(body)}\" with title \"{AppleScriptEscape(summary)}\"";
                var psi = new ProcessStartInfo(OsascriptPath) { UseShellExecute = false, CreateNoWindow = true };
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add(script);
                using var proc = Process.Start(psi);
                if (proc is null) return;
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Hung osascript — nothing safe to do; the OS usually still delivers or drops it.
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UnoVibe: osascript toast failed: {ex.Message}");
            }
        }

        /// <summary>Escapes a value for a double-quoted AppleScript string literal.</summary>
        private static string AppleScriptEscape(string value)
        {
            // Newlines aren't valid inside a one-line AppleScript literal; the facade already
            // collapses them, but keep this defensive for direct callers.
            value = value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}

namespace Microsoft.Windows.AppNotifications.Builder
{
    /// <summary>Polifill of <c>Microsoft.Windows.AppNotifications.Builder.AppNotificationBuilder</c>
    /// — fluently collects the toast's text lines and produces an <see cref="AppNotification"/>.
    /// WASDK renders AddText lines as heading then body; the polyfill keeps that order (first line =
    /// heading, rest = body) so the macOS notification looks like the WinUI toast.</summary>
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