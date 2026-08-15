using System.ComponentModel;
using System.Diagnostics;

namespace UnoVibe.Services;

/// <summary>
/// Launches external programs against a local folder: the default file manager
/// (Explorer on Windows, the platform's <c>xdg-open</c>/<c>open</c> equivalent elsewhere),
/// the default terminal, and the configured editor/IDE (the "Default IDE/Editor" setting,
/// default VS Code's <c>code</c> CLI). Returns an error message on failure, or null on success.
/// </summary>
public static class FolderLauncher
{
    /// <summary>Opens <paramref name="folder"/> in the OS file manager. Returns an error message or null.</summary>
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

    /// <summary>
    /// Opens <paramref name="folder"/> in the configured editor/IDE — the "Default IDE/Editor"
    /// setting (<see cref="SettingsStore.EditorCommand"/>), run as <c>&lt;command&gt; &lt;folder&gt;</c>.
    /// An empty/cleared setting falls back to VS Code's <c>code</c> CLI. Returns an error message or null.
    /// </summary>
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
            // On Windows the command may resolve via the shell (e.g. code.cmd); on Unix it execs the wrapper script.
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

    /// <summary>Opens the best available terminal at <paramref name="folder"/>. Returns an error message or null.</summary>
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

    /// <summary>
    /// Opens a terminal on Windows, preferring Windows Terminal (which honors the user's own
    /// default profile via <c>-d</c>), then PowerShell, then cmd. For the shells the working
    /// directory comes from the process itself, so no <c>cd</c> command is ever issued.
    /// </summary>
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

    /// <summary>Opens macOS's default terminal (Terminal.app) at <paramref name="folder"/>.</summary>
    private static string? OpenMacTerminal(string folder)
    {
        var psi = new ProcessStartInfo("open") { UseShellExecute = false };
        psi.ArgumentList.Add("-a");
        psi.ArgumentList.Add("Terminal");
        psi.ArgumentList.Add(folder);
        Process.Start(psi);
        return null;
    }

    /// <summary>
    /// Opens the first available Linux terminal emulator at <paramref name="folder"/>. Linux has
    /// no standard "default terminal" command, so common emulators are probed in order. xterm is
    /// the last resort (its <c>-e</c> launches bash with the folder as the inherited cwd).
    /// </summary>
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

    /// <summary>Starts <paramref name="fileName"/> with the given working directory and arguments.</summary>
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

    /// <summary>Checks whether <paramref name="command"/> resolves on <c>PATH</c> (or as a rooted path) without spawning a shell.</summary>
    private static bool IsCommandAvailable(string command)
    {
        if (Path.IsPathRooted(command))
            return File.Exists(command);

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return false;

        // On Windows a bare name like "code" resolves through PATHEXT (code.cmd / code.exe).
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
