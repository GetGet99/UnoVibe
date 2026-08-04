using UnoVibe.Models;

namespace UnoVibe.Controls.ToolViews;

[QuickMarkup("""
    using UnoVibe.Models;
    using QuickMarkup.WinUI;
    PartItem Part;
    bool Expanded = false;
    bool Hovering = false;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <StackPanel Spacing=4>
        <Button Background=`Hovering ? theme.SystemNeutralBackground : theme.SubtleFill` CornerRadius=4 Padding=`new Thickness(8, 4, 8, 4)` BorderThickness=0 HorizontalContentAlignment=Left HorizontalAlignment=Stretch Click+=`(s, e) => Expanded = !Expanded` PointerEntered+=`(s, e) => Hovering = true` PointerExited+=`(s, e) => Hovering = false`>
            <StackPanel Orientation=Horizontal Spacing=8>
                <ToolBusyIndicator Part=`Part` />
                <TextBlock Text=`Expanded ? "▾" : "▸"` FontSize=12 FontFamily="Consolas" Foreground=`Hovering ? theme.PrimaryText : theme.SecondaryText` VerticalAlignment=Center />
                <TextBlock Text=`ToolViewShared.EditTitle(Part)` FontSize=12 FontFamily="Consolas" Foreground=`theme.PrimaryText` TextWrapping=Wrap IsTextSelectionEnabled=true VerticalAlignment=Center />
            </StackPanel>
        </Button>
        if (`Expanded`)
        {
            if (`Part.Diff.Length > 0`)
                <Border Background=`theme.SolidBackground` CornerRadius=4 Padding=`new Thickness(8, 6, 8, 6)`>
                    <TextBlock Text=`ToolViewShared.Truncate(Part.Diff, 6000)` FontSize=12 FontFamily="Consolas" TextWrapping=Wrap IsTextSelectionEnabled=true />
                </Border>
            if (`Part.ToolOutput.Length > 0`)
                <TextBlock Text=`ToolViewShared.Truncate(Part.ToolOutput, 4000)` FontSize=11 FontFamily="Consolas" Foreground=`theme.SecondaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
        }
        if (`Part.ToolError.Length > 0`)
            <Border Background=`theme.SystemCriticalBackground` CornerRadius=4 Padding=`new Thickness(8, 6, 8, 6)`>
                <TextBlock Text=`Part.ToolError` FontSize=11 FontFamily="Consolas" Foreground=`theme.SystemCritical` TextWrapping=Wrap IsTextSelectionEnabled=true />
            </Border>
    </StackPanel>
    """)]
public partial class ToolViewEdit : IQuickMarkupComponent;