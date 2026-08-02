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
        <TextBlock Text=`ToolViewShared.Read(Part)` FontSize=12 FontFamily="Consolas" Foreground=`theme.SecondaryText` TextWrapping=Wrap />
        if (`Part.LoadedFiles.Length > 0`)
            <TextBlock Text=`ToolViewShared.Truncate(ToolViewShared.Loaded(Part), 2000)` FontSize=11 FontFamily="Consolas" Foreground=`theme.SecondaryText` TextWrapping=Wrap />
        if (`Part.ToolError.Length > 0`)
            <TextBlock Text=`Part.ToolError` FontSize=11 FontFamily="Consolas" Foreground=`theme.SystemCritical` TextWrapping=Wrap />
    </StackPanel>
    """)]
public partial class ToolViewRead : IQuickMarkupComponent;
