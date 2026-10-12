using QuickMarkup.Infra.Collections;

namespace UnoVibe.Pages.Main;

[QuickMarkup("""
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Microsoft.UI;
    inject SessionsStateProvider Sessions;
    inject HomeProvider Home;
    inject EventsProvider Events;
    inject ToastsProvider Toasts;
    inject Window HostWindow;
    inject bool SettingsOpen;
    inject bool IsCompact;
    inject bool IsSidebarView;
    inject OpencodeConnection Connection;
    <setup>
        var theme = ThemeBrushes.Global;
        var transparent = new SolidColorBrush(Colors.Transparent);
    </setup>
    <root>
        <Grid Background=`theme.CardBackground` BorderBrush=`theme.DividerStroke` BorderThickness=`SessionSidebarBorder` RowDefinitions=<>
            <RowDefinition />
            <RowDefinition Height=Auto />
            <RowDefinition Height=Auto />
        </>>
            <ScrollViewer Grid.Row=0>
                <StackPanel Padding=`new Thickness(12, 0, 12, 12)`>
                    <Button
                        Margin=`new Thickness(0, 8, 0, 0)`
                        Padding=`new Thickness(8, 6, 8, 6)`
                        HorizontalAlignment=Stretch HorizontalContentAlignment=Stretch
                        @Click+=`Home.GoHome(); IsSidebarView = false`
                        Background=`Home.IsHomeView ? theme.ControlFill : transparent`
                        ToolTipService.ToolTip="Pinned chats">
                        <Grid ColumnDefinitions=<>
                            <ColumnDefinition Width=Auto />
                            <ColumnDefinition />
                            <ColumnDefinition Width=Auto />
                        </>>
                            <AppSymbolIcon Symbol=Home FontSize=12 Foreground=`theme.SecondaryText` Margin=`new Thickness(0, 0, 6, 0)` VerticalAlignment=Center />
                            <TextBlock Grid.Column=1 Text="Home" FontSize=12 VerticalAlignment=Center />
                            if (`Home.PinnedCount > 0`)
                            {
                                <TextBlock Grid.Column=2 Text=`Home.PinnedCount.ToString()` FontSize=10 Foreground=`theme.TertiaryText` Margin=`new Thickness(8, 0, 0, 0)` VerticalAlignment=Center />
                            }
                        </Grid>
                    </Button>
                    foreach (var group in `Sessions.SessionSidebar`; `group.Directory`)
                    {
                        <SessionGroup Group=`group` />
                    }
                </StackPanel>
            </ScrollViewer>

            <McpStatusView />

            <Border Grid.Row=2 Padding=`new Thickness(12, 8, 12, 10)` BorderBrush=`theme.DividerStroke` BorderThickness=`new Thickness(0, 1, 0, 0)`>
                <Grid ColumnDefinitions=<>
                    <ColumnDefinition />
                    <ColumnDefinition Width=Auto />
                </>>
                    <TextBlock Text=`Connection.ConnectionStatus` FontSize=11 Foreground=`theme.SecondaryText` TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                    <StackPanel Grid.Column=1 Orientation=Horizontal Spacing=8 VerticalAlignment=Center>
                        <SmallButtonIcon Symbol=Folder Tooltip="Open Folder" @Click+=`OpenFolderAndStartSessionAsync()` />
                        <SmallButtonIcon Symbol=NewWindow Tooltip="New window" @Click+=`App.CreateWindow()` />
                        <SmallButtonIcon Symbol=Setting Tooltip="Settings" @Click+=`SettingsOpen = true` />
                        <SmallButtonIcon Symbol=More Tooltip="Connection details" Flyout=<ConnectionFlyout /> />
                    </StackPanel>
                </Grid>
            </Border>
        </Grid>
    </root>
    """)]
partial class SessionSidebar : IQuickMarkupComponent
{
    Thickness SessionSidebarBorder =>
#if WASDK
        new(0, 1, 1, 0)
#else
        new(0, 0, 1, 0)
#endif
        ;

    private async Task OpenFolderAndStartSessionAsync()
    {
        try
        {
            var path = await WindowsHelper.PickFolderAsync(HostWindow, Connection.ServerDirectory);
            if (path is null) return;
            Home.IsHomeView = false;
            Sessions.PrepareNewSession(path);
            await Sessions.AddDirectoryAsync(path);
            IsSidebarView = false;
        }
        catch (Exception ex)
        {
            Toasts.ShowError(ex.Message, "Folder picker failed");
        }
    }
}
