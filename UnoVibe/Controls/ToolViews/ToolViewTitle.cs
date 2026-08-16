using UnoVibe.Models;

namespace UnoVibe.Controls.ToolViews;

/// <summary>
/// Tool header row: shows a spinning ring while the tool is running, followed by
/// the title text. Mirrors the TUI's per-tool spinner while a call is in flight.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Models;
    using QuickMarkup.WinUI;
    using UnoVibe.Services;
    required PartItem Part;
    string Text = "";
    bool SemiBold = false;
    bool IsShell = false;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        <StackPanel Orientation=Horizontal Spacing=8>
            <ToolBusyIndicator Part=`Part` />
            <TextBlock Text=`Text` FontSize=12
                       FontWeight=`SemiBold ? FontWeights.SemiBold : FontWeights.Normal`
                       FontFamily=`IsShell ? CodeFonts.Current : DefaultFont`
                       Foreground=`IsShell ? theme.PrimaryText : theme.SecondaryText`
                       TextWrapping=Wrap IsTextSelectionEnabled=true VerticalAlignment=Center />
            if (`Part.Interrupted`)
                <Border Background=`theme.SystemCautionBackground` CornerRadius=4 Padding=`new Thickness(5, 1, 5, 2)` VerticalAlignment=Center>
                    <TextBlock Text="interrupted" FontSize=10 Foreground=`theme.SystemCaution` VerticalAlignment=Center />
                </Border>
        </StackPanel>
    </root>
    """)]
public partial class ToolViewTitle : IQuickMarkupComponent<UIElement>
{
    public static FontFamily DefaultFont => new("Segoe UI Variable");
}
