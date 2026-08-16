using UnoVibe.Models;

namespace UnoVibe.Controls.ToolViews;

[QuickMarkup("""
    using UnoVibe.Models;
    using QuickMarkup.WinUI;
    required PartItem Part;
    bool Expanded = false;
    bool Hovering = false;
    bool PlainMode = false;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <StackPanel Spacing=4 MaxWidth=720 HorizontalAlignment=Left>
        <Button Background=`Hovering ? theme.SystemNeutralBackground : theme.SubtleFill` CornerRadius=4 Padding=`new Thickness(8, 4, 8, 4)` BorderThickness=0 HorizontalContentAlignment=Left HorizontalAlignment=Stretch Click+=`(s, e) => Expanded = !Expanded` PointerEntered+=`(s, e) => Hovering = true` PointerExited+=`(s, e) => Hovering = false`>
            <StackPanel Orientation=Horizontal Spacing=8>
                if (`!Part.Time.IsDone`)
                    <ProgressRing Width=14 Height=14 IsActive=true Foreground=`theme.SystemCaution` VerticalAlignment=Center />
                <TextBlock Text=`Expanded ? "▾" : "▸"` FontSize=12 Foreground=`Hovering ? theme.PrimaryText : theme.SecondaryText` VerticalAlignment=Center />
                <TextBlock Text=`Part.Time.IsDone ? ToolViewShared.ThoughtLabel(Part) : ToolViewShared.ReasoningLabel(Part)` FontSize=12 FontWeight=`FontWeights.SemiBold` TextWrapping=Wrap Foreground=`Part.Time.IsDone ? theme.SecondaryText : theme.SystemCaution` VerticalAlignment=Center />
            </StackPanel>
        </Button>
        if (`Expanded`)
        {
            if (`ToolViewShared.ReasoningSummary(Part).Body.Length > 0`)
            {
                <Border Background=`theme.SolidBackground` CornerRadius=4 Padding=`new Thickness(8, 6, 8, 6)`>
                    <MarkdownView Text=`ToolViewShared.ReasoningSummary(Part).Body` PlainMode=`PlainMode` />
                </Border>
                <Button Width=26 Height=22 Padding=0 CornerRadius=5 Background=`theme.SubtleFill` BorderThickness=0
                        HorizontalAlignment=Left
                        ToolTipService.ToolTip=`PlainMode ? "Show formatted Markdown" : "Show plain text"`
                        @Click+=`PlainMode = !PlainMode`>
                    <AppSymbolIcon Symbol=`PlainMode ? Symbol.Font : Symbol.Bullets` FontSize=11 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                </Button>
            }
        }
    </StackPanel>
    """)]
public partial class ToolViewReasoning : IQuickMarkupComponent;