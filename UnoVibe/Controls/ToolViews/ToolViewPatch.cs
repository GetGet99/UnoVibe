using UnoVibe.Models;

namespace UnoVibe.Controls.ToolViews;

/// <summary>
/// Renders an <c>apply_patch</c> tool call (OpenAI-style models emit this instead of
/// <c>edit</c>). Mirrors the TUI's per-file "Created/Deleted/Moved/Patched" blocks and
/// the web client's "Patch" card: one bordered block per patched file with a label,
/// add/delete counts, and the unified diff. Falls back to the raw combined diff when the
/// server omits the per-file metadata (older servers only surface <c>state.metadata.diff</c>).
/// </summary>
[QuickMarkup("""
    using UnoVibe.Models;
    using UnoVibe.Controls.ToolViews;
    using QuickMarkup.WinUI;
    using UnoVibe.Services;
    required PartItem Part;
    bool Expanded = false;
    bool Hovering = false;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <StackPanel Spacing=4>
        <Button Background=`Hovering ? theme.SystemNeutralBackground : theme.SubtleFill` CornerRadius=4 Padding=`new Thickness(8, 4, 8, 4)` BorderThickness=0 HorizontalContentAlignment=Left HorizontalAlignment=Stretch Click+=`(s, e) => Expanded = !Expanded` PointerEntered+=`(s, e) => Hovering = true` PointerExited+=`(s, e) => Hovering = false`>
            <StackPanel Orientation=Horizontal Spacing=8>
                <ToolBusyIndicator Part=`Part` />
                <TextBlock Text=`Expanded ? "▾" : "▸"` FontSize=12 Foreground=`Hovering ? theme.PrimaryText : theme.SecondaryText` VerticalAlignment=Center />
                <TextBlock Text=`ToolViewShared.Patch(Part)` FontSize=12 Foreground=`theme.PrimaryText` TextWrapping=Wrap IsTextSelectionEnabled=true VerticalAlignment=Center />
            </StackPanel>
        </Button>
        if (`Expanded`)
        {
            foreach (var f in `ToolViewShared.ParsePatchFiles(Part)`)
                <Border Background=`theme.SolidBackground` CornerRadius=4 Padding=`new Thickness(8, 6, 8, 6)` Margin=`new Thickness(0, 2, 0, 2)`>
                    <StackPanel Spacing=4>
                        <TextBlock Text=`ToolViewShared.PatchFileLine(f)` FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.PrimaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
                        if (`f.Type == "delete"`)
                            <TextBlock Text=`f.Deletions > 0 ? $"-{f.Deletions} line" + (f.Deletions == 1 ? "" : "s") : "Deleted"` FontSize=12 FontFamily=`CodeFonts.Current` Foreground=`theme.SystemCritical` TextWrapping=Wrap IsTextSelectionEnabled=true />
                        else if (`f.Patch.Length > 0`)
                            <DiffView Diff=`f.Patch` />
                    </StackPanel>
                </Border>
            if (`Part.Diff.Length > 0 && ToolViewShared.ParsePatchFiles(Part).Count == 0`)
                <DiffView Diff=`Part.Diff` />
        }
        if (`Part.ToolError.Length > 0`)
            <Border Background=`theme.SystemCriticalBackground` CornerRadius=4 Padding=`new Thickness(8, 6, 8, 6)`>
                <TextBlock Text=`Part.ToolError` FontSize=11 Foreground=`theme.SystemCritical` TextWrapping=Wrap IsTextSelectionEnabled=true />
            </Border>
    </StackPanel>
    """)]
public partial class ToolViewPatch : IQuickMarkupComponent;
