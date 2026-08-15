using UnoVibe.Models;
using UnoVibe.Services;

namespace UnoVibe.Pages.Connect;

/// <summary>
/// The "Recent" connections card on the left of the connect page: the recently-opened folder
/// and server entries with open/remove actions, plus the empty state and "Clear all". Buttons
/// disable while a connection is in progress; opening/removing an entry is delegated back to
/// the page via <see cref="OpenRecentRequested"/> / <see cref="RemoveRecentRequested"/>.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Services;
    using UnoVibe.Models;
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Microsoft.UI;
    inject bool Connecting;
    <setup>
        var theme = ThemeBrushes.Global;
        var transparent = new SolidColorBrush(Colors.Transparent);
    </setup>
    <root>
        <Border Background=`theme.CardBackground` BorderBrush=`theme.CardStroke` BorderThickness=1 CornerRadius=8 Padding=`new Thickness(16, 14, 16, 14)`>
            <StackPanel Spacing=10>
                <Grid ColumnDefinitions=<>
                    <ColumnDefinition />
                    <ColumnDefinition Width=Auto />
                </> ColumnSpacing=8>
                    <TextBlock Text="Recent" FontSize=14 FontWeight=`FontWeights.SemiBold` VerticalAlignment=Center />
                    <Button Grid.Column=1 Content="Clear all" FontSize=11 Padding=`new Thickness(6, 3, 6, 3)` Background=`transparent` BorderThickness=0 Visibility=`RecentConnectionsStore.Items.Reactive.Count > 0 ? Visibility.Visible : Visibility.Collapsed` @Click+=`RecentConnectionsStore.ClearAll()` ToolTipService.ToolTip="Remove all recent entries" />
                </Grid>
                <ScrollViewer Height=300 VerticalScrollBarVisibility=Auto>
                    if (`RecentConnectionsStore.Items.Reactive.Count == 0`)
                    {
                        <StackPanel Spacing=6 Padding=`new Thickness(0, 12, 0, 0)`>
                            <TextBlock Text="No recent connections yet." FontSize=13 FontWeight=`FontWeights.SemiBold` Foreground=`theme.SecondaryText` />
                            <TextBlock Text="Folders and servers you open will appear here." FontSize=12 Foreground=`theme.TertiaryText` TextWrapping=Wrap />
                        </StackPanel>
                    }
                    else
                    {
                        <StackPanel Spacing=2>
                            foreach (var item in `RecentConnectionsStore.Items`; `item.Key`)
                            {
                                <Grid ColumnDefinitions=<>
                                    <ColumnDefinition />
                                    <ColumnDefinition Width=Auto />
                                </> ColumnSpacing=10>
                                    <Button Grid.Column=0 HorizontalAlignment=Stretch HorizontalContentAlignment=Left Background=`transparent` BorderThickness=0 Padding=`new Thickness(6, 6, 6, 6)` CommandParameter=`item` Click+=`(sender, e) => OnOpenRecent(sender, e)` IsEnabled=`!Connecting`>
                                        <StackPanel Orientation=Horizontal Spacing=8>
                                            <AppSymbolIcon Symbol=`item.IsFolder ? Symbol.Folder : Symbol.Globe` FontSize=14 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                                            <StackPanel Spacing=1>
                                                <TextBlock Text=`item.Display` FontSize=13 FontWeight=`FontWeights.SemiBold` TextTrimming=`TextTrimming.CharacterEllipsis` />
                                                <TextBlock Text=`item.Detail` FontSize=11 Foreground=`theme.TertiaryText` TextTrimming=`TextTrimming.CharacterEllipsis` />
                                            </StackPanel>
                                        </StackPanel>
                                    </Button>
                                    <Button Grid.Column=1 Padding=`new Thickness(8, 4, 8, 4)` VerticalAlignment=Center Background=`transparent` BorderThickness=0 CommandParameter=`item.Key` Click+=`(sender, e) => OnRemoveRecent(sender, e)` ToolTipService.ToolTip="Remove from recent" IsEnabled=`!Connecting`>
                                        <AppSymbolIcon Symbol=Cancel FontSize=10 Foreground=`theme.TertiaryText` />
                                    </Button>
                                </Grid>
                            }
                        </StackPanel>
                    }
                </ScrollViewer>
            </StackPanel>
        </Border>
    </root>
    """)]
public partial class RecentListPanel : IQuickMarkupComponent<Border>
{
    /// <summary>Handler for <see cref="OpenRecentRequested"/>.</summary>
    public delegate Task OpenRecentHandler(RecentConnection item);

    /// <summary>Raised when the user clicks a recent entry; the page runs the connect/serve flow.</summary>
    public event OpenRecentHandler? OpenRecentRequested;

    /// <summary>Raised with the entry key when the user removes a recent entry.</summary>
    public event Action<string>? RemoveRecentRequested;

    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();
    }

    private void OnOpenRecent(object sender, RoutedEventArgs e)
    {
        if (Connecting || (sender as Button)?.CommandParameter is not RecentConnection item) return;
        if (OpenRecentRequested is not null) _ = OpenRecentRequested(item);
    }

    private void OnRemoveRecent(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string key) return;
        RemoveRecentRequested?.Invoke(key);
    }
}
