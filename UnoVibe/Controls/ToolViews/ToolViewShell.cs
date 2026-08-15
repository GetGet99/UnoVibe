using UnoVibe.Models;

namespace UnoVibe.Controls.ToolViews;

[QuickMarkup("""
    using UnoVibe.Models;
    using QuickMarkup.WinUI;
    required PartItem Part;
    bool Expanded = false;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <StackPanel Spacing=4>
        <Border Background=`Part.ToolStatus == "error" ? theme.SystemCriticalBackground : theme.SubtleFill` CornerRadius=4 Padding=`new Thickness(8, 4, 8, 4)`>
            <ToolViewTitle Part=`Part` Text=`ToolViewShared.Shell(Part)` SemiBold=true IsShell=true />
        </Border>
        if (`Part.ShellOutput.Length > 0`)
        {
            <Border Background=`theme.LayerFill` CornerRadius=4 Padding=`new Thickness(8, 6, 8, 6)`>
                <TextBlock Text=`Expanded ? Part.ShellOutput : ToolViewShared.ShellCollapsed(Part)` FontSize=12 FontFamily="Consolas" TextWrapping=Wrap IsTextSelectionEnabled=true />
            </Border>
            if (`ToolViewShared.ShellOverflow(Part)`)
                <Button Background=`theme.LayerFill` BorderThickness=0 CornerRadius=4 Padding=`new Thickness(8, 2, 8, 2)` HorizontalAlignment=Left Click+=`(s, e) => Expanded = !Expanded`>
                    <TextBlock Text=`Expanded ? "Show less ▴" : "Show more ▾"` FontSize=11 FontFamily="Consolas" Foreground=`theme.TertiaryText` />
                </Button>
        }
        if (`Part.ToolError.Length > 0`)
            <Border Background=`theme.SystemCriticalBackground` CornerRadius=4 Padding=`new Thickness(8, 6, 8, 6)`>
                <TextBlock Text=`Part.ToolError` FontSize=12 FontFamily="Consolas" Foreground=`theme.SystemCritical` TextWrapping=Wrap IsTextSelectionEnabled=true />
            </Border>
    </StackPanel>
    """)]
public partial class ToolViewShell : IQuickMarkupComponent;