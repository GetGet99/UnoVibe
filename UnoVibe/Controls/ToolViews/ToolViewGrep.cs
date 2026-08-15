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
        <ToolViewTitle Part=`Part` Text=`ToolViewShared.Grep(Part)` />
        if (`Part.ToolError.Length > 0`)
            <TextBlock Text=`Part.ToolError` FontSize=11 FontFamily="Consolas" Foreground=`theme.SystemCritical` TextWrapping=Wrap IsTextSelectionEnabled=true />
    </StackPanel>
    """)]
public partial class ToolViewGrep : IQuickMarkupComponent;
