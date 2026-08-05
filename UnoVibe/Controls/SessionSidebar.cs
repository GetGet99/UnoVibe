using UnoVibe.Services;
using UnoVibe.Models;

namespace UnoVibe.Controls;

/// <summary>
/// Left sidebar listing sessions grouped by directory, with per-group "new session" buttons.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Services;
    using UnoVibe.Models;
    using QuickMarkup.WinUI;
    using Microsoft.UI;
    inject ChatStore Store;
    <setup>
        var theme = ThemeBrushes.Global;
        var transparent = new SolidColorBrush(Colors.Transparent);
    </setup>
    <root>
        <Grid Background=`theme.CardBackground` BorderBrush=`theme.DividerStroke` BorderThickness=`new Thickness(0, 0, 1, 0)` RowDefinitions=<>
            <RowDefinition Height=Auto />
            <RowDefinition />
            <RowDefinition Height=Auto />
        </>>
            <StackPanel Padding=`new Thickness(12, 12, 12, 8)` Spacing=8>
                <Button Click+=`(sender, e) => OnNewSession(sender, e)` HorizontalAlignment=Stretch>
                    <StackPanel Orientation=Horizontal Spacing=6>
                        <AppSymbolIcon Symbol=Add FontSize=13 VerticalAlignment=Center />
                        <TextBlock Text="New session" VerticalAlignment=Center />
                    </StackPanel>
                </Button>
                <Button Content="New window" Click+=`(sender, e) => OnNewWindow(sender, e)` HorizontalAlignment=Stretch />
            </StackPanel>
            <ScrollViewer Grid.Row=1>
                <StackPanel Padding=`new Thickness(12, 0, 12, 12)`>
                    foreach (var group in `Store.DirectoryGroups`)
                    {
                        <StackPanel Margin=`new Thickness(0, 12, 0, 0)`>
                            <Grid ColumnDefinitions=<>
                                <ColumnDefinition />
                                <ColumnDefinition Width=Auto />
                            </>>
                                <TextBlock Text=`group.Directory` FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.SecondaryText` TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                                <Button Grid.Column=1 Padding=`new Thickness(6, 4, 6, 4)` CommandParameter=`group.Directory` Click+=`(sender, e) => OnNewSession(sender, e)`>
                                    <AppSymbolIcon Symbol=Add FontSize=11 />
                                </Button>
                            </Grid>
                            foreach (var s in `group.Sessions`)
                            {
                                <Button Margin=`new Thickness(0, 4, 0, 0)` Padding=`new Thickness(8, 6, 8, 6)` HorizontalAlignment=Stretch HorizontalContentAlignment=Left CommandParameter=`s.Id` Click+=`(sender, e) => OnSwitchSession(sender, e)` Background=`Store.ActiveSessionId == s.Id ? theme.ControlFill : transparent`>
                                    <Grid ColumnDefinitions=<>
                                        <ColumnDefinition Width=Auto />
                                        <ColumnDefinition />
                                        <ColumnDefinition Width=Auto />
                                    </>>
                                        <Grid Width=14 Margin=`new Thickness(0, 0, 6, 0)` VerticalAlignment=Center>
                                            <AppSymbolIcon Symbol=`AttentionSymbol(s)` FontSize=10 Foreground=`theme.SystemAttention` Visibility=`s.NeedsAttention ? Visibility.Visible : Visibility.Collapsed` HorizontalAlignment=Center VerticalAlignment=Center />
                                            <ProgressRing Width=12 Height=12 IsActive=`s.IsBusy` Visibility=`!s.NeedsAttention && s.IsBusy ? Visibility.Visible : Visibility.Collapsed` HorizontalAlignment=Center VerticalAlignment=Center />
                                            <AppSymbolIcon Symbol=`OutcomeSymbol(s)` FontSize=10 Foreground=`OutcomeBrush(s)` Visibility=`!s.NeedsAttention && s.IsUnread && !s.IsBusy && s.Outcome.Length > 0 ? Visibility.Visible : Visibility.Collapsed` HorizontalAlignment=Center VerticalAlignment=Center />
                                            <Border Width=6 Height=6 CornerRadius=`new CornerRadius(3)` Background=`theme.SystemAttention` Visibility=`!s.NeedsAttention && s.IsUnread && !s.IsBusy && s.Outcome.Length == 0 ? Visibility.Visible : Visibility.Collapsed` HorizontalAlignment=Center VerticalAlignment=Center />
                                            <AppSymbolIcon Symbol=Message FontSize=10 Foreground=`theme.TertiaryText` Visibility=`!s.NeedsAttention && !s.IsBusy && !s.IsUnread ? Visibility.Visible : Visibility.Collapsed` HorizontalAlignment=Center VerticalAlignment=Center />
                                        </Grid>
                                        <TextBlock Grid.Column=1 Text=`s.Title` FontSize=12 TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                                        <TextBlock Grid.Column=2 Text=`s.TimeLabel` FontSize=10 Foreground=`theme.TertiaryText` Margin=`new Thickness(8, 0, 0, 0)` VerticalAlignment=Center />
                                    </Grid>
                                </Button>
                            }
                        </StackPanel>
                    }
                </StackPanel>
            </ScrollViewer>
            <Border Grid.Row=2 Padding=`new Thickness(12, 8, 12, 10)` BorderBrush=`theme.DividerStroke` BorderThickness=`new Thickness(0, 1, 0, 0)`>
                <TextBlock Text=`Store.ConnectionStatus` FontSize=11 Foreground=`theme.SecondaryText` TextTrimming=`TextTrimming.CharacterEllipsis` />
            </Border>
        </Grid>
    </root>
    """)]
public partial class SessionSidebar : IQuickMarkupComponent
{
    private void OnSwitchSession(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string id) return;
        if (id == Store.CurrentSessionId) return;
        _ = Store.SwitchSessionAsync(id);
    }

    private void OnNewSession(object sender, RoutedEventArgs e)
    {
        var directory = (sender as Button)?.CommandParameter as string;
        _ = Store.NewSessionAsync(directory);
    }

    private void OnNewWindow(object sender, RoutedEventArgs e) => UnoVibe.App.CreateWindow();

    /// <summary>Icon for an unread session's turn outcome: check = success, X = error, stop = interrupted.</summary>
    private static Symbol OutcomeSymbol(SessionInfo s) => s.Outcome switch
    {
        "error" => Symbol.Cancel,
        "interrupted" => Symbol.Stop,
        _ => Symbol.Accept,
    };

    /// <summary>Color for <see cref="OutcomeSymbol"/>: green success, red error, caution interrupted.</summary>
    private static Brush? OutcomeBrush(SessionInfo s) => s.Outcome switch
    {
        "error" => ThemeBrushes.Global.SystemCritical,
        "interrupted" => ThemeBrushes.Global.SystemCaution,
        _ => ThemeBrushes.Global.SystemSuccess,
    };

    /// <summary>Glyph for a pending question/approval: shield for a permission, question mark for a question.</summary>
    private static Symbol AttentionSymbol(SessionInfo s) => s.AttentionKind == "permission" ? Symbol.Permissions : Symbol.Help;
}
