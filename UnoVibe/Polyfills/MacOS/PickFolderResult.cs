#if DESKTOP_MACOS
// Registers the macOS polyfill result type as the app-wide `PickFolderResult`.
// See AGENTS.md -> "Polyfills".
global using PickFolderResult = UnoVibe.Polyfills.MacOS.PickFolderResult;

namespace UnoVibe.Polyfills.MacOS;

/// <summary>
/// Result of a folder picking operation, mirroring the Windows App SDK's
/// <c>Microsoft.Windows.Storage.Pickers.PickFolderResult</c> (a lightweight object carrying the
/// picked folder path). A cancelled dialog is surfaced as a null result instead.
/// </summary>
public sealed class PickFolderResult
{
    internal PickFolderResult(string path) => Path = path;

    /// <summary>The path of the folder selected by the user.</summary>
    public string Path { get; }
}
#endif