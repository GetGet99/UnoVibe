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
}
