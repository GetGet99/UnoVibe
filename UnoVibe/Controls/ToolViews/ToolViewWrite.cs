using UnoVibe.Models;

namespace UnoVibe.Controls.ToolViews;

[QuickMarkup("""
    using UnoVibe.Models;
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
                <TextBlock Text=`ToolViewShared.WriteTitle(Part)` FontSize=12 Foreground=`theme.PrimaryText` TextWrapping=Wrap IsTextSelectionEnabled=true VerticalAlignment=Center />
            </StackPanel>
        </Button>
        if (`Expanded`)
        {
            if (`Part.ToolContent.Length > 0`)
                <CodeView Text=`Part.ToolContent` FilePath=`Part.ToolFilePath` />
            else if (`Part.ToolOutput.Length > 0`)
                <Border Background=`theme.SolidBackground` CornerRadius=4 Padding=`new Thickness(8, 6, 8, 6)`>
                    <TextBlock Text=`ToolViewShared.Truncate(Part.ToolOutput, 4000)` FontSize=12 FontFamily=`CodeFonts.Current` TextWrapping=Wrap IsTextSelectionEnabled=true />
                </Border>
        }
        if (`Part.ToolError.Length > 0`)
            <Border Background=`theme.SystemCriticalBackground` CornerRadius=4 Padding=`new Thickness(8, 6, 8, 6)`>
                <TextBlock Text=`Part.ToolError` FontSize=11 Foreground=`theme.SystemCritical` TextWrapping=Wrap IsTextSelectionEnabled=true />
            </Border>
    </StackPanel>
    """)]
public partial class ToolViewWrite : IQuickMarkupComponent;