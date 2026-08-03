using UnoVibe.Models;

namespace UnoVibe.Controls.ToolViews;

[QuickMarkup("""
    using UnoVibe.Models;
    using QuickMarkup.WinUI;
    PartItem Part;
    bool Expanded = false;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <StackPanel Spacing=4>
        <Border Background=`Part.ToolStatus == "error" ? theme.SystemCriticalBackground : theme.SubtleFill` CornerRadius=4 Padding=`new Thickness(8, 4, 8, 4)`>
            <TextBlock Text=`ToolViewShared.Shell(Part)` FontSize=12 FontWeight=`FontWeights.SemiBold` FontFamily="Consolas" TextWrapping=Wrap IsTextSelectionEnabled=true />
        </Border>
        if (`Part.ShellOutput.Length > 0`)
        {
            <Button Background=`theme.SolidBackground` CornerRadius=4 Padding=`new Thickness(8, 6, 8, 6)` BorderThickness=0 HorizontalContentAlignment=Left HorizontalAlignment=Stretch Click+=`(s, e) => Expanded = !Expanded`>
                <TextBlock Text=`Expanded ? Part.ShellOutput : ToolViewShared.ShellCollapsed(Part)` FontSize=12 FontFamily="Consolas" TextWrapping=Wrap IsTextSelectionEnabled=true />
            </Button>
            if (`ToolViewShared.ShellOverflow(Part)`)
                <TextBlock Text=`Expanded ? "Click to collapse" : "Click to expand"` FontSize=11 FontFamily="Consolas" Foreground=`theme.TertiaryText` />
        }
        if (`Part.ToolError.Length > 0`)
            <Border Background=`theme.SystemCriticalBackground` CornerRadius=4 Padding=`new Thickness(8, 6, 8, 6)`>
                <TextBlock Text=`Part.ToolError` FontSize=12 FontFamily="Consolas" Foreground=`theme.SystemCritical` TextWrapping=Wrap IsTextSelectionEnabled=true />
            </Border>
    </StackPanel>
    """)]
public partial class ToolViewShell : IQuickMarkupComponent;
