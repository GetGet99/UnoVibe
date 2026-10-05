#if DESKTOP_LINUX
namespace UnoVibe.Polyfills.Linux;

sealed class PickFolderResult
{
    internal PickFolderResult(string path) => Path = path;

    public string Path { get; }
}
#endif