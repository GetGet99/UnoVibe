namespace UnoVibe.Helpers;

static class PathDisplayHelper
{
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
                    return relative == "." ? folderName : $"{folderName} ({relative})";
                }
                return relative;
            }
        }
        catch { }
        return fullPath;
    }
}