using UnoVibe.Models;

namespace UnoVibe.Controls.ToolViews;

[QuickMarkup("""
    using UnoVibe.Models;
    using QuickMarkup.WinUI;
    required PartItem Part;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <StackPanel Spacing=4>
        <ToolViewTitle Part=`Part` Text=`ToolViewShared.Generic(Part)` />
        if (`Part.ToolOutput.Length > 0`)
            <Border Background=`theme.SolidBackground` CornerRadius=4 Padding=`new Thickness(8, 6, 8, 6)`>
                <TextBlock Text=`ToolViewShared.Truncate(Part.ToolOutput, 4000)` FontSize=12 FontFamily="Consolas" TextWrapping=Wrap IsTextSelectionEnabled=true />
            </Border>
        if (`Part.ToolError.Length > 0`)
            <TextBlock Text=`Part.ToolError` FontSize=11 FontFamily="Consolas" Foreground=`theme.SystemCritical` TextWrapping=Wrap IsTextSelectionEnabled=true />
    </StackPanel>
    """)]
public partial class ToolViewGeneric : IQuickMarkupComponent;
