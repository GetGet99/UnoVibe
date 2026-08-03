using UnoVibe.Models;

namespace UnoVibe.Controls.ToolViews;

/// <summary>
/// Tool header row: shows a spinning ring while the tool is running, followed by
/// the title text. Mirrors the TUI's per-tool spinner while a call is in flight.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Models;
    using QuickMarkup.WinUI;
    PartItem Part;
    string Text = "";
    bool SemiBold = false;
    bool IsShell = false;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        <StackPanel Orientation=Horizontal Spacing=8>
            if (`Part.ToolStatus == "running"`)
                <ProgressRing Width=14 Height=14 IsActive=true Foreground=`theme.SystemCaution` VerticalAlignment=Center />
            <TextBlock Text=`Text` FontSize=12
                       FontWeight=`SemiBold ? FontWeights.SemiBold : FontWeights.Normal`
                       FontFamily="Consolas"
                       Foreground=`IsShell ? theme.PrimaryText : theme.SecondaryText`
                       TextWrapping=Wrap IsTextSelectionEnabled=true VerticalAlignment=Center />
        </StackPanel>
    </root>
    """)]
public partial class ToolViewTitle : IQuickMarkupComponent<UIElement>;
