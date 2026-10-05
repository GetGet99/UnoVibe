using QuickMarkup.Infra.Collections;

namespace UnoVibe.Pages.Main;

[QuickMarkup("""
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Microsoft.UI;
    inject SessionsStateProvider Sessions;
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
                    if (`IsCompact`)
                    {
                        <Button HorizontalAlignment=Left Margin=`new Thickness(0, 8, 0, 0)` Padding=`new Thickness(6, 4, 10, 4)`
                                Background=`transparent` BorderThickness=0 CornerRadius=6 Foreground=`theme.SecondaryText`
                                @Click+=`IsSidebarView = false` ToolTipService.ToolTip="Back to chat">
                            <StackPanel Orientation=Horizontal Spacing=6>
                                <AppSymbolIcon Symbol=Back FontSize=12 />
                                <TextBlock Text="Back to chat" FontSize=12 />
                            </StackPanel>
                        </Button>
                    }
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
                    <ColumnDefinition Width=Auto />
                    <ColumnDefinition Width=Auto />
                    <ColumnDefinition Width=Auto />
                </>>
                    <TextBlock Text=`Connection.ConnectionStatus` FontSize=11 Foreground=`theme.SecondaryText` TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                    <Button Grid.Column=1 Margin=`new Thickness(8, 0, 0, 0)` Padding=`new Thickness(6, 4, 6, 4)` ToolTipService.ToolTip="Open Folder" @Click+=`OpenFolderAndStartSessionAsync()`>
                        <AppSymbolIcon Symbol=Folder FontSize=11 />
                    </Button>
                    <Button Grid.Column=2 Margin=`new Thickness(8, 0, 0, 0)` Padding=`new Thickness(6, 4, 6, 4)` ToolTipService.ToolTip="New window" @Click+=`App.CreateWindow()`>
                        <AppSymbolIcon Symbol=NewWindow FontSize=11 />
                    </Button>
                    <Button Grid.Column=3 Margin=`new Thickness(8, 0, 0, 0)` Padding=`new Thickness(6, 4, 6, 4)` ToolTipService.ToolTip="Settings" @Click+=`SettingsOpen = true`>
                        <AppSymbolIcon Symbol=Setting FontSize=11 />
                    </Button>
                    <Button Grid.Column=4 Margin=`new Thickness(8, 0, 0, 0)` Padding=`new Thickness(6, 4, 6, 4)` ToolTipService.ToolTip="Connection details" Flyout=<ConnectionFlyout />>
                        <AppSymbolIcon Symbol=More FontSize=11 />
                    </Button>
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
