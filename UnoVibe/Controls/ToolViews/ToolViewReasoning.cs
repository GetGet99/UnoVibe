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
        <Button Background=`theme.SubtleFill` CornerRadius=4 Padding=`new Thickness(8, 4, 8, 4)` BorderThickness=0 HorizontalContentAlignment=Left HorizontalAlignment=Stretch Click+=`(s, e) => Expanded = !Expanded`>
            <StackPanel Orientation=Horizontal Spacing=8>
                if (`!Part.Time.IsDone`)
                    <ProgressRing Width=14 Height=14 IsActive=true Foreground=`theme.SystemCaution` VerticalAlignment=Center />
                <TextBlock Text=`Part.Time.IsDone ? ToolViewShared.ThoughtLabel(Part, Expanded) : ToolViewShared.ReasoningLabel(Part)` FontSize=12 FontWeight=`FontWeights.SemiBold` FontFamily="Consolas" TextWrapping=Wrap Foreground=`Part.Time.IsDone ? theme.SecondaryText : theme.SystemCaution` IsTextSelectionEnabled=true VerticalAlignment=Center />
            </StackPanel>
        </Button>
        if (`Expanded && Part.Time.IsDone`)
        {
            if (`ToolViewShared.ReasoningSummary(Part).Body.Length > 0`)
                <Border Background=`theme.SolidBackground` CornerRadius=4 Padding=`new Thickness(8, 6, 8, 6)`>
                    <TextBlock Text=`ToolViewShared.ReasoningSummary(Part).Body` FontSize=11 FontFamily="Consolas" Foreground=`theme.SecondaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
                </Border>
        }
    </StackPanel>
    """)]
public partial class ToolViewReasoning : IQuickMarkupComponent;
