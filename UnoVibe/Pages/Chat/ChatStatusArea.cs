namespace UnoVibe.Pages.Chat;

/// <summary>
/// Chat page status strip below the header: the retry/status banner and the horizontal
/// strip of active subagent chips (busy ring, attention glyph, turn-outcome icon).
/// </summary>
[QuickMarkup("""
    using UnoVibe.Controls;
    using UnoVibe.States;
    using QuickMarkup.WinUI;
    inject bool IsCompact;
    inject ChatMessagesState? ChatState;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        <StackPanel Padding=`new Thickness(IsCompact ? 12 : 16, 0, IsCompact ? 12 : 16, 4)` Spacing=6>
            if (`ChatState?.Retry.IsRetrying == true`)
                <Border Background=`theme.SystemCautionBackground` CornerRadius=6 Padding=`new Thickness(10,  6, 10,  6)`
                        BorderBrush=`theme.SystemCaution` BorderThickness=`new Thickness(1)` HorizontalAlignment=Stretch>
                    <StackPanel Orientation=Horizontal Spacing=8>
                        <ProgressRing Width=14 Height=14 IsActive=true VerticalAlignment=Center />
                        <TextBlock Text=`FormatStatusMessage()` FontSize=12 Foreground=`theme.SystemCaution` TextWrapping=Wrap IsTextSelectionEnabled=true VerticalAlignment=Center />
                    </StackPanel>
                </Border>
            if (`StoreToUpdate.SubagentCount > 0`)
            {
                <StackPanel Spacing=6>
                    <TextBlock Text=`$"Subagents ({StoreToUpdate.SubagentCount})"` FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.SecondaryText` />
                    <ScrollViewer HorizontalScrollBarVisibility=Auto VerticalScrollBarVisibility=Disabled>
                        <StackPanel Orientation=Horizontal Spacing=6>
                            foreach (var s in `StoreToUpdate.ActiveSubagents`; `s.Head.Id`)
                            {
                                <Button Padding=`new Thickness(10,  6, 10,  6)` CornerRadius=6 Background=`theme.CardBackground` BorderBrush=`theme.CardStroke` BorderThickness=1
                                        @Click+=`await StoreToUpdate.SwitchSessionAsync(s.Id)`
                                        ToolTipService.ToolTip=`s.Head.Title`>
                                    <StackPanel Orientation=Horizontal Spacing=6>
                                        <SessionIndicator State=`s.Head.State` />
                                        <TextBlock Text=`s.Head.Title` FontSize=12 TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                                    </StackPanel>
                                </Button>
                            }
                        </StackPanel>
                    </ScrollViewer>
                </StackPanel>
            }
        </StackPanel>
    </root>
    """)]
public partial class ChatStatusArea : IQuickMarkupComponent<StackPanel>
{
    string FormatStatusMessage()
    {
        var retry = ChatState?.Retry;
        if (retry is null || !retry.IsRetrying) return "";
        var prefix = retry.Attempt > 0 ? $"Retry #{retry.Attempt}" : "Retry";
        return retry.Message.Length > 0 ? $"{prefix}: {retry.Message}" : prefix;
    }
}
