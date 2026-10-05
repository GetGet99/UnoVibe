#if DESKTOP_MACOS
global using PickFolderResult = UnoVibe.Polyfills.MacOS.PickFolderResult;

namespace UnoVibe.Polyfills.MacOS;

sealed class PickFolderResult
{
    internal PickFolderResult(string path) => Path = path;

    public string Path { get; }
}
#endif