using UnoVibe.Models;
using UnoVibe.Services;

namespace UnoVibe.Pages.Chat;

/// <summary>
/// Chat page status strip below the header: the retry/status banner and the horizontal
/// strip of active subagent chips (busy ring, attention glyph, turn-outcome icon).
/// </summary>
[QuickMarkup("""
    using UnoVibe.Services;
    using UnoVibe.Models;
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    inject ChatStore Store;
    inject? bool IsCompact;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        <StackPanel Padding=`new Thickness(IsCompact ? 12 : 16, 0, IsCompact ? 12 : 16, 4)` Spacing=6>
            if (`Store.Active.StatusMessage.Length > 0`)
                <Border Background=`theme.SystemCautionBackground` CornerRadius=6 Padding=`new Thickness(10,  6, 10,  6)`
                        BorderBrush=`theme.SystemCaution` BorderThickness=`new Thickness(1)` HorizontalAlignment=Stretch>
                    <StackPanel Orientation=Horizontal Spacing=8>
                        <ProgressRing Width=14 Height=14 IsActive=true VerticalAlignment=Center />
                        <TextBlock Text=`Store.Active.StatusMessage` FontSize=12 Foreground=`theme.SystemCaution` TextWrapping=Wrap IsTextSelectionEnabled=true VerticalAlignment=Center />
                    </StackPanel>
                </Border>
            if (`Store.SubagentCount > 0`)
            {
                <StackPanel Spacing=6>
                    <TextBlock Text=`$"Subagents ({Store.SubagentCount})"` FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.SecondaryText` />
                    <ScrollViewer HorizontalScrollBarVisibility=Auto VerticalScrollBarVisibility=Disabled>
                        <StackPanel Orientation=Horizontal Spacing=6>
                            foreach (var s in `Store.ActiveSubagents`; `s.Id`)
                            {
                                <Button Padding=`new Thickness(10,  6, 10,  6)` CornerRadius=6 Background=`theme.CardBackground` BorderBrush=`theme.CardStroke` BorderThickness=1
                                        @Click+=`await Store.SwitchSessionAsync(s.Id)`
                                        ToolTipService.ToolTip=`s.Title`>
                                    <StackPanel Orientation=Horizontal Spacing=6>
                                        <SessionIndicator State=`s.State` />
                                        <TextBlock Text=`s.Title` FontSize=12 TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
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
    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();
    }
}
