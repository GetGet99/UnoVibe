using UnoVibe.Models;

namespace UnoVibe.Controls.ToolViews;

public static class ToolViewShared
{
    public static string Shell(PartItem p) =>
        p.ToolCommand.Length > 0
            ? "$ " + p.ToolCommand + (p.ToolWorkdir.Length > 0 ? "  (in " + p.ToolWorkdir + ")" : "")
            : p.ToolTitle ?? p.ToolName ?? "shell";

    public static string Glob(PartItem p)
    {
        var name = p.ToolPattern.Length > 0 ? "Glob \"" + p.ToolPattern + "\"" : p.ToolTitle ?? p.ToolName ?? "glob";
        var count = p.MatchCount.Length > 0 ? " (" + p.MatchCount + " match" + (p.MatchCount == "1" ? "" : "es") + ")" : "";
        return "✱ " + name + count;
    }

    public static string Read(PartItem p) =>
        "→ " + (p.ToolFilePath.Length > 0 ? "Read " + p.ToolFilePath : p.ToolTitle ?? p.ToolName ?? "read");

    public static string Loaded(PartItem p)
    {
        if (p.LoadedFiles.Length == 0) return "";
        return string.Join("\n", p.LoadedFiles.Split('\n').Select(l => "↳ Loaded " + l));
    }

    public static string Edit(PartItem p) =>
        "← " + (p.ToolFilePath.Length > 0 ? "Edit " + p.ToolFilePath : p.ToolTitle ?? p.ToolName ?? "edit");

    public static string Write(PartItem p) =>
        "← " + (p.ToolFilePath.Length > 0 ? "Write " + p.ToolFilePath : p.ToolTitle ?? p.ToolName ?? "write");

    public static string Generic(PartItem p)
    {
        var name = p.ToolTitle ?? p.ToolName ?? "tool";
        var input = p.ToolInput.Length > 0 && p.ToolInput.Length <= 400 ? " " + p.ToolInput : "";
        return "⚙ " + name + input;
    }

    public static string Truncate(string value, int max)
    {
        if (value.Length <= max) return value;
        return value.Substring(0, max) + "\n… (truncated, " + (value.Length - max) + " more chars)";
    }
}
