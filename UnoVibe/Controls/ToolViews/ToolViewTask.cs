using UnoVibe.Models;
using UnoVibe.Services;

namespace UnoVibe.Controls.ToolViews;

/// <summary>
/// Renders a subagent-spawning <c>task</c> tool call as a clickable card. The card shows the
/// task description, the subagent type, and a live status (from the part's streamed state);
/// clicking it opens the subagent's own session (part.state.metadata.sessionId). Subagent
/// sessions are hidden from the sidebar, so this card is the entry point to view them.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Models;
    using UnoVibe.Services;
    using UnoVibe.Controls.ToolViews;
    using QuickMarkup.WinUI;
    inject ChatStore Store;
    required PartItem Part;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <Button Background=`theme.CardBackground` BorderBrush=`theme.CardStroke` BorderThickness=1 CornerRadius=8
            Padding=`new Thickness(10,  8, 10,  8)` HorizontalAlignment=Left MaxWidth=680 Margin=`new Thickness(0, 2, 0, 2)`
            IsEnabled=`Part.ToolSessionId.Length > 0`
            ToolTipService.ToolTip=`Part.ToolSessionId.Length > 0 ? "Open the subagent session" : "Waiting for the subagent session…"`
            @Click+=`await OpenAsync()`>
        <Grid ColumnSpacing=8 ColumnDefinitions=<>
            <ColumnDefinition Width=Auto />
            <ColumnDefinition />
            <ColumnDefinition Width=Auto />
        </>>
            <Grid Width=14 Height=14 VerticalAlignment=Center>
                <ToolBusyIndicator Part=`Part` />
            </Grid>
            <StackPanel Grid.Column=1 Spacing=2 VerticalAlignment=Center>
                <StackPanel Orientation=Horizontal Spacing=8>
                    <TextBlock Text=`ToolViewShared.Task(Part)` FontSize=12 FontFamily="Consolas" FontWeight=`FontWeights.SemiBold`
                               Foreground=`theme.SecondaryText` TextWrapping=Wrap VerticalAlignment=Center />
                    if (`Part.ToolSubagentType.Length > 0`)
                        <Border Background=`theme.SubtleFill` CornerRadius=4 Padding=`new Thickness(6, 1, 6, 2)` VerticalAlignment=Center>
                            <TextBlock Text=`Part.ToolSubagentType` FontSize=10 FontFamily="Consolas" Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                        </Border>
                </StackPanel>
                <TextBlock Text=`ToolViewShared.TaskStatus(Part)` FontSize=11 Foreground=`theme.TertiaryText` TextWrapping=Wrap />
            </StackPanel>
            <StackPanel Grid.Column=2 Orientation=Horizontal Spacing=6 VerticalAlignment=Center>
                if (`Part.ToolStatus == "completed"`)
                    <TextBlock Text="✓" FontSize=12 Foreground=`theme.SystemSuccess` VerticalAlignment=Center />
                else if (`Part.ToolStatus == "error"`)
                    <TextBlock Text="⚠" FontSize=12 Foreground=`theme.SystemCritical` VerticalAlignment=Center />
                <AppSymbolIcon Symbol=Forward FontSize=11 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
            </StackPanel>
        </Grid>
    </Button>
    """)]
public partial class ToolViewTask : IQuickMarkupComponent
{
    private async Task OpenAsync()
    {
        if (Part.ToolSessionId.Length == 0) return;
        await Store.SwitchSessionAsync(Part.ToolSessionId);
    }
}
