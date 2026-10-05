#if DESKTOP_MACOS
using System.Diagnostics;
using UnoVibe.Polyfills.MacOS;

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
        private const string OsascriptPath = "/usr/bin/osascript";

        private static string? _terminalNotifierPath;

        private AppNotificationManager()
        {
        }

        public static AppNotificationManager Default { get; } = new();

        public bool Register() => true;

        public void Show(AppNotification notification)
        {
            var texts = notification.Texts;
            var summary = texts.Count > 0 ? texts[0] : string.Empty;
            var body = texts.Count > 1 ? texts[1] : string.Empty;
            _ = SendAsync(summary, body);
        }

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
                System.Diagnostics.Debug.WriteLine($"UnoVibe: toast failed: {ex.Message}");
            }
        }

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
            _terminalNotifierPath = string.Empty;
            return null;
        }

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
                    try { proc.Kill(entireProcessTree: true); } catch {   }
                    return false;
                }
                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private static async Task RunOsascriptAsync(string summary, string body)
        {
            try
            {
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
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UnoVibe: osascript toast failed: {ex.Message}");
            }
        }

        private static string AppleScriptEscape(string value)
        {
            value = value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
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