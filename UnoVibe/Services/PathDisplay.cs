using System.IO;

namespace UnoVibe.Services;

/// <summary>
/// Shared folder-path display helper used by the sidebar and the chat header. Shows the
/// shorter of the full path or a path relative to the connected server's directory — the
/// meaningful "home" for the user, not the app's CWD — so callers only pass the reference
/// directory and get consistent output everywhere.
/// </summary>
public static class PathDisplay
{
    /// <summary>
    /// Returns the shorter of <paramref name="fullPath"/> or a path relative to
    /// <paramref name="referenceDir"/>. Falls back to the current directory when no reference
    /// directory is known. For parent directories (dot-only relative paths), shows
    /// "FolderName (../..)" so the user can see at a glance how far up the path goes.
    /// </summary>
    public static string Relative(string fullPath, string referenceDir)
    {
        if (string.IsNullOrEmpty(fullPath)) return fullPath;
        try
        {
            var reference = referenceDir.Length > 0 ? referenceDir : Directory.GetCurrentDirectory();
            var relative = Path.GetRelativePath(reference, fullPath);
            if (!Path.IsPathRooted(relative) && relative.Length < fullPath.Length)
            {
                var segments = relative.Split(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                var isDotOnly = segments.All(s => s is "." or "..");
                if (isDotOnly)
                {
                    var folderName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                                     ?? relative;
                    // "." means same directory — just show the name. ".." and above — append the hint.
                    return relative == "." ? folderName : $"{folderName} ({relative})";
                }
                return relative;
            }
        }
        catch { }
        return fullPath;
    }
}