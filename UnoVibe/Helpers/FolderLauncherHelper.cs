using System.ComponentModel;
using System.Diagnostics;

namespace UnoVibe.Helpers;

static class FolderLauncherHelper
{
    public static string? OpenInFileManager(string folder)
    {
        if (!Directory.Exists(folder)) return $"Folder not found locally: {folder}";
        try
        {
#if WINDOWS
            {
                var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
                psi.ArgumentList.Add(folder);
                Process.Start(psi);
            }
#elif DESKTOP_MACOS
            {
                var psi = new ProcessStartInfo("open") { UseShellExecute = false };
                psi.ArgumentList.Add(folder);
                Process.Start(psi);
            }
#else
            {
                var psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                psi.ArgumentList.Add(folder);
                Process.Start(psi);
            }
#endif
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public static string? OpenInEditor(string folder)
    {
        if (!Directory.Exists(folder)) return $"Folder not found locally: {folder}";
        var command = SettingsStore.EditorCommand.Trim();
        if (command.Length == 0)
        {
            command = IsCommandAvailable("code") ? "code" : "";
            if (command.Length == 0) return "No editor command configured — set one in Settings.";
        }
        if (!IsCommandAvailable(command)) return $"Editor command \"{command}\" not found on PATH.";
        try
        {
            var psi = new ProcessStartInfo(command)
            {
#if WINDOWS
                UseShellExecute = true
#else
                UseShellExecute = false
#endif
            };
            psi.ArgumentList.Add(folder);
            Process.Start(psi);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public static string? OpenUrl(string url)
    {
        try
        {
#if WINDOWS
            {
                var psi = new ProcessStartInfo(url) { UseShellExecute = true };
                Process.Start(psi);
            }
#elif DESKTOP_MACOS
            {
                var psi = new ProcessStartInfo("open") { UseShellExecute = false };
                psi.ArgumentList.Add(url);
                Process.Start(psi);
            }
#else
            {
                var psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                psi.ArgumentList.Add(url);
                Process.Start(psi);
            }
#endif
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public static string? OpenInTerminal(string folder)
    {
        if (!Directory.Exists(folder)) return $"Folder not found locally: {folder}";
        try
        {
#if WINDOWS
            return OpenWindowsTerminal(folder);
#elif DESKTOP_MACOS
            return OpenMacTerminal(folder);
#else
            return OpenLinuxTerminal(folder);
#endif
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return $"Could not open terminal: {ex.Message}";
        }
    }

    private static string? OpenWindowsTerminal(string folder)
    {
        if (IsCommandAvailable("wt.exe"))
        {
            StartProcess("wt.exe", folder, "-d", folder);
            return null;
        }

        var shell = IsCommandAvailable("pwsh.exe") ? "pwsh.exe"
            : IsCommandAvailable("powershell.exe") ? "powershell.exe"
            : "cmd.exe";
        StartProcess(shell, folder);
        return null;
    }

    private static string? OpenMacTerminal(string folder)
    {
        var psi = new ProcessStartInfo("open") { UseShellExecute = false };
        psi.ArgumentList.Add("-a");
        psi.ArgumentList.Add("Terminal");
        psi.ArgumentList.Add(folder);
        Process.Start(psi);
        return null;
    }

    private static string? OpenLinuxTerminal(string folder)
    {
        var terminals = new[]
        {
            ("gnome-terminal", new[] { "--working-directory", folder }),
            ("konsole", new[] { "--workdir", folder }),
            ("xfce4-terminal", new[] { "--working-directory", folder }),
            ("mate-terminal", new[] { "--working-directory", folder }),
            ("xterm", new[] { "-e", "bash" }),
        };

        foreach (var (command, arguments) in terminals)
        {
            if (!IsCommandAvailable(command)) continue;
            StartProcess(command, folder, arguments);
            return null;
        }

        return "No supported terminal emulator found.";
    }

    private static void StartProcess(string fileName, string workingDirectory, params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        Process.Start(psi);
    }

    private static bool IsCommandAvailable(string command)
    {
        if (Path.IsPathRooted(command))
            return File.Exists(command);

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return false;

#if WINDOWS
        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
#else
        string[]? extensions = null;
#endif

        foreach (var directory in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            string candidate = Path.Combine(directory, command);
            if (File.Exists(candidate)) return true;
            if (extensions is null) continue;
            foreach (var extension in extensions)
            {
                if (File.Exists(candidate + extension)) return true;
            }
        }

        return false;
    }
}
