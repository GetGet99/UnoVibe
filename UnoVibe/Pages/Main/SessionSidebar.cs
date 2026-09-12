using UnoVibe.Services;
using QuickMarkup.Infra.Collections;

namespace UnoVibe.Pages.Main;

/// <summary>
/// Left sidebar listing sessions grouped by directory, with per-group "new session" buttons.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Services;
    using UnoVibe.Providers;
    using UnoVibe.Models;
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Microsoft.UI;
    inject SessionsSource Sessions;
    inject EventSource Events;
    inject ToastService Toasts;
    inject Window HostWindow;
    inject bool SettingsOpen;
    inject? bool IsCompact;
    inject? bool IsSidebarView;
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
                        <StackPanel Margin=`new Thickness(0, 12, 0, 0)`>
                            <Grid ColumnDefinitions=<>
                                <ColumnDefinition />
                                <ColumnDefinition Width=Auto />
                            </> ColumnSpacing=4>
                                <StackPanel Orientation=Horizontal Spacing=4>
                                    <TextBlock Text=`DisplayPath(group.Directory)` FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.SecondaryText` TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                                    if (`group.Branch is null`)
                                    {
                                        <TextBlock Text=`$"⎇ {group.Branch}"` FontSize=10 Foreground=`theme.TertiaryText` TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                                    }
                                </StackPanel>
                                // TODO: When attached property support falling back to element properly, do that instead of wrapping in Grid.
                                <Grid Grid.Column=1 VerticalAlignment=Center>
                                    <FolderActions Directory=`group.Directory` />
                                </Grid>
                            </Grid>
                            if (`group.Sessions.Count == 0`)
                            {
                                <TextBlock Text="No sessions yet" FontSize=11 Foreground=`theme.TertiaryText` Margin=`new Thickness(0, 6, 0, 0)` />
                            }
                            foreach (var s in `ShowMoreDirectories.Contains(group.Directory) ? group.Sessions : group.Sessions.Take(MaxVisibleSessions)`; `s.Head.Id`)
                            {
                                <SessionButton Session=`s.Head` />
                            }
                            if (`group.Sessions.Count > MaxVisibleSessions`)
                            {
                                <Button Margin=`new Thickness(0, 4, 0, 0)` Padding=`new Thickness(8, 4, 8, 4)` HorizontalAlignment=Left Background=`transparent` BorderThickness=0 @Click+=`OnToggleShowMore(group.Directory)`>
                                    <TextBlock Text=`group.IsExpanded ? "Show less" : $"Show more ({group.Sessions.Count - MaxVisibleSessions})"` FontSize=11 Foreground=`theme.SecondaryText` />
                                </Button>
                            }
                        </StackPanel>
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
                    <TextBlock Text=`Store.ConnectionStatus` FontSize=11 Foreground=`theme.SecondaryText` TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
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
public partial class SessionSidebar : IQuickMarkupComponent
{
    ReactiveSet<string> ShowMoreDirectories = [];
    Thickness SessionSidebarBorder =>
#if WASDK
        // WASDK title bar have the same mica color as body so would make sense to have top border too
        new(0, 1, 1, 0)
#else
        new(0, 0, 1, 0)
#endif
        ;
    /// <summary>Number of sessions shown per directory group before the "Show more" toggle appears.</summary>
    private const int MaxVisibleSessions = 5;

    private void OnToggleShowMore(string directory)
    {
        // if unable to remove, then add it!
        // yes this is toggle logic
        if (!ShowMoreDirectories.Remove(directory))
            ShowMoreDirectories.Add(directory);
    }

    /// <summary>
    /// Opens a folder picker and starts a new session in the picked folder. The session is
    /// created lazily on the first message send, so no empty server-side session is produced.
    /// </summary>
    private async Task OpenFolderAndStartSessionAsync()
    {
        try
        {
            var path = await WindowsHelper.PickFolderAsync(HostWindow, Connection.ServerDirectory);
            if (path is not null)
            {
                Sessions.PrepareNewSession(path);
                // Small-screen view switching: opening a folder lands in its new chat view.
                IsSidebarView = false;
            }
        }
        catch (Exception ex)
        {
            Toasts.ShowError(ex.Message, "Folder picker failed");
        }
    }

    /// <summary>
    /// Path relative to the connected server's directory via <see cref="PathDisplay.Relative"/>.
    /// </summary>
    private string DisplayPath(string fullPath) => PathDisplay.Relative(fullPath, Connection.ServerDirectory);
}
