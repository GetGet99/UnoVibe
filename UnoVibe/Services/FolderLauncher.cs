using System.Diagnostics;

namespace UnoVibe.Services;

/// <summary>
/// Launches external programs against a local folder: the default file manager
/// (Explorer on Windows, the platform's <c>xdg-open</c>/<c>open</c> equivalent elsewhere)
/// and VS Code's <c>code</c> CLI. Returns an error message on failure, or null on success.
/// </summary>
public static class FolderLauncher
{
    /// <summary>Opens <paramref name="folder"/> in the OS file manager. Returns an error message or null.</summary>
    public static string? OpenInFileManager(string folder)
    {
        if (!Directory.Exists(folder)) return $"Folder not found locally: {folder}";
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
                psi.ArgumentList.Add(folder);
                Process.Start(psi);
            }
            else
            {
                var psi = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false };
                psi.ArgumentList.Add(folder);
                Process.Start(psi);
            }
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Opens <paramref name="folder"/> in VS Code (the <c>code</c> CLI). Returns an error message or null.</summary>
    public static string? OpenInVSCode(string folder)
    {
        if (!Directory.Exists(folder)) return $"Folder not found locally: {folder}";
        try
        {
            // On Windows `code` resolves via the shell (code.cmd); on Unix it execs the wrapper script.
            var psi = new ProcessStartInfo("code") { UseShellExecute = OperatingSystem.IsWindows() };
            psi.ArgumentList.Add(folder);
            Process.Start(psi);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
