using UnoVibe.Services;

namespace UnoVibe.Controls;

/// <summary>
/// Left sidebar listing sessions grouped by directory, with per-group "new session" buttons.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Services;
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
                <Button Content="+ New session" Click+=`(sender, e) => OnNewSession(sender, e)` HorizontalAlignment=Stretch />
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
                                <Button Grid.Column=1 Content="+" Padding=`new Thickness(8, 2, 8, 2)` CommandParameter=`group.Directory` Click+=`(sender, e) => OnNewSession(sender, e)` />
                            </Grid>
                            foreach (var s in `group.Sessions`)
                            {
                                <Button Margin=`new Thickness(0, 4, 0, 0)` Padding=`new Thickness(8, 6, 8, 6)` HorizontalAlignment=Stretch HorizontalContentAlignment=Left CommandParameter=`s.Id` Click+=`(sender, e) => OnSwitchSession(sender, e)` Background=`Store.ActiveSessionId == s.Id ? theme.ControlFill : transparent`>
                                    <Grid ColumnDefinitions=<>
                                        <ColumnDefinition />
                                        <ColumnDefinition Width=Auto />
                                    </>>
                                        <TextBlock Text=`s.Title` FontSize=12 TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                                        <TextBlock Grid.Column=1 Text=`s.TimeLabel` FontSize=10 Foreground=`theme.TertiaryText` Margin=`new Thickness(8, 0, 0, 0)` VerticalAlignment=Center />
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
}
