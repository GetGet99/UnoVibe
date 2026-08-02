using UnoVibe.Models;

namespace UnoVibe.Controls.ToolViews;

[QuickMarkup("""
    using UnoVibe.Models;
    using QuickMarkup.WinUI;
    PartItem Part;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <StackPanel Spacing=4>
        <TextBlock Text=`ToolViewShared.Generic(Part)` FontSize=12 FontFamily="Consolas" Foreground=`theme.SecondaryText` TextWrapping=Wrap />
        if (`Part.ToolOutput.Length > 0`)
            <Border Background=`theme.SolidBackground` CornerRadius=4 Padding=`new Thickness(8, 6, 8, 6)`>
                <TextBlock Text=`ToolViewShared.Truncate(Part.ToolOutput, 4000)` FontSize=12 FontFamily="Consolas" TextWrapping=Wrap />
            </Border>
        if (`Part.ToolError.Length > 0`)
            <TextBlock Text=`Part.ToolError` FontSize=11 FontFamily="Consolas" Foreground=`theme.SystemCritical` TextWrapping=Wrap />
    </StackPanel>
    """)]
public partial class ToolViewGeneric : IQuickMarkupComponent;
