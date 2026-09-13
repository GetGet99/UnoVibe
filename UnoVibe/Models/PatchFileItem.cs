namespace UnoVibe.Models;

/// <summary>
/// One file touched by an <c>apply_patch</c> tool call, parsed from the part's
/// <c>state.metadata.files</c> array. Mirrors the metadata the opencode server emits
/// per patched file: absolute <see cref="FilePath"/>, worktree-relative path, the
/// add/update/delete/move kind, the unified diff, and line counts.
/// </summary>
public sealed class PatchFileItem
{
    public string Type = "";      // "add" | "update" | "delete" | "move"
    public string RelativePath = "";
    public string FilePath = "";
    public string Patch = "";
    public string MovePath = "";
    public int Additions;
    public int Deletions;

    public static string PatchFileLine(PatchFileItem f)
    {
        var label = f.Type switch
        {
            "add" => "# Created ",
            "delete" => "# Deleted ",
            "move" => "# Moved " + (f.FilePath.Length > 0 ? f.FilePath + " → " : ""),
            _ => "← Patched ",
        };
        var text = label + f.RelativePath;
        if (f.Additions + f.Deletions > 0)
            text += $"  ({f.Additions}+ {f.Deletions}-)";
        return text;
    }
}
