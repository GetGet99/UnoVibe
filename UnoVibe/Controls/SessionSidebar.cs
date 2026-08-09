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
    using QuickMarkup.Infra.Collections;
    using Microsoft.UI;
    inject ChatStore Store;
    bool McpExpanded = false;
    <setup>
        var theme = ThemeBrushes.Global;
        var transparent = new SolidColorBrush(Colors.Transparent);
    </setup>
    <root>
        <Grid Background=`theme.CardBackground` BorderBrush=`theme.DividerStroke` BorderThickness=`new Thickness(0, 0, 1, 0)` RowDefinitions=<>
            <RowDefinition />
            <RowDefinition Height=Auto />
            <RowDefinition Height=Auto />
        </>>
            <ScrollViewer Grid.Row=0>
                <StackPanel Padding=`new Thickness(12, 0, 12, 12)`>
                    foreach (var group in `Store.DirectoryGroups`; `group.Directory`)
                    {
                        <StackPanel Margin=`new Thickness(0, 12, 0, 0)`>
                            <Grid ColumnDefinitions=<>
                                <ColumnDefinition />
                                <ColumnDefinition Width=Auto />
                                <ColumnDefinition Width=Auto />
                                <ColumnDefinition Width=Auto />
                            </> ColumnSpacing=4>
                                <TextBlock Text=`group.Directory` FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.SecondaryText` TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                                <Button Grid.Column=1 Padding=`new Thickness(6, 4, 6, 4)` CommandParameter=`group.Directory` ToolTipService.ToolTip="Open folder in VS Code" Click+=`(sender, e) => OnOpenInVSCode(sender, e)`>
                                    <AppSymbolIcon Symbol=`Symbol.Code` FontSize=11 />
                                </Button>
                                <Button Grid.Column=2 Padding=`new Thickness(6, 4, 6, 4)` CommandParameter=`group.Directory` ToolTipService.ToolTip="Open folder in file manager" Click+=`(sender, e) => OnOpenInFileManager(sender, e)`>
                                    <AppSymbolIcon Symbol=OpenLocal FontSize=11 />
                                </Button>
                                <Button Grid.Column=3 Padding=`new Thickness(6, 4, 6, 4)` CommandParameter=`group.Directory` Click+=`(sender, e) => OnNewSession(sender, e)`>
                                    <AppSymbolIcon Symbol=Add FontSize=11 />
                                </Button>
                            </Grid>
                            if (`group.Sessions.Reactive.Count == 0`)
                            {
                                <TextBlock Text="No sessions yet" FontSize=11 Foreground=`theme.TertiaryText` Margin=`new Thickness(0, 6, 0, 0)` />
                            }
                            foreach (var s in `group.IsExpanded ? group.Sessions.Reactive : group.Sessions.Reactive.Take(MaxVisibleSessions)`; `s.Id`)
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
                            if (`group.Sessions.Reactive.Count > MaxVisibleSessions`)
                            {
                                <Button Margin=`new Thickness(0, 4, 0, 0)` Padding=`new Thickness(8, 4, 8, 4)` HorizontalAlignment=Left Background=`transparent` BorderThickness=0 CommandParameter=`group.Directory` Click+=`(sender, e) => OnToggleShowMore(sender, e)`>
                                    <TextBlock Text=`group.IsExpanded ? "Show less" : $"Show more ({group.Sessions.Reactive.Count - MaxVisibleSessions})"` FontSize=11 Foreground=`theme.SecondaryText` />
                                </Button>
                            }
                        </StackPanel>
                    }
                </StackPanel>
            </ScrollViewer>
            <Border Grid.Row=1 Padding=`new Thickness(12, 8, 12, 8)` BorderBrush=`theme.DividerStroke` BorderThickness=`new Thickness(0, 1, 0, 0)`>
                <StackPanel Spacing=6>
                    <Grid ColumnDefinitions=<>
                        <ColumnDefinition />
                        <ColumnDefinition Width=Auto />
                    </> ColumnSpacing=8>
                        <Button Padding=`new Thickness(4, 2, 4, 2)` HorizontalAlignment=Left Background=`transparent` BorderThickness=0 @Click+=`OnToggleMcpExpanded()` ToolTipService.ToolTip="MCP servers">
                            <StackPanel Orientation=Horizontal Spacing=6>
                                <TextBlock Text=`McpExpanded ? "▼" : "▶"` FontSize=9 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
                                <TextBlock Text="MCP" FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                                <TextBlock Text=`Store.McpSummary` FontSize=10 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
                            </StackPanel>
                        </Button>
                        <Button Grid.Column=1 Padding=`new Thickness(6, 3, 6, 3)` @Click+=`_ = Store.RefreshMcpStatusAsync()` ToolTipService.ToolTip="Refresh MCP status" Visibility=`McpExpanded ? Visibility.Visible : Visibility.Collapsed`>
                            <AppSymbolIcon Symbol=Refresh FontSize=11 />
                        </Button>
                    </Grid>
                    if (`McpExpanded`)
                    {
                        <ScrollViewer MaxHeight=200 VerticalScrollBarVisibility=Auto>
                            <StackPanel Spacing=6>
                                foreach (var m in `Store.McpServers`; `m.Name`)
                                {
                                    <Grid ColumnDefinitions=<>
                                        <ColumnDefinition Width=Auto />
                                        <ColumnDefinition />
                                        <ColumnDefinition Width=Auto />
                                    </> ColumnSpacing=8>
                                        <Border Width=10 Height=10 CornerRadius=`new CornerRadius(5)` Background=`McpDot(m)` VerticalAlignment=Center ToolTipService.ToolTip=`m.Error` />
                                        <StackPanel Grid.Column=1 VerticalAlignment=Center>
                                            <TextBlock Text=`m.Name` FontSize=12 TextTrimming=`TextTrimming.CharacterEllipsis` />
                                            <TextBlock Text=`McpStatusDetail(m)` FontSize=10 Foreground=`theme.TertiaryText` TextTrimming=`TextTrimming.CharacterEllipsis` />
                                        </StackPanel>
                                        <Button Grid.Column=2 Padding=`new Thickness(8, 4, 8, 4)` FontSize=11 Content=`m.ToggleLabel` IsEnabled=`!m.Connecting` CommandParameter=`m.Name` Click+=`(sender, e) => OnToggleMcp(sender, e)` />
                                    </Grid>
                                }
                            </StackPanel>
                        </ScrollViewer>
                        <TextBlock Text=`$"Directory: {Store.McpDirectory}"` FontSize=10 Foreground=`theme.TertiaryText` TextTrimming=`TextTrimming.CharacterEllipsis` />
                    }
                </StackPanel>
            </Border>
            <Border Grid.Row=2 Padding=`new Thickness(12, 8, 12, 10)` BorderBrush=`theme.DividerStroke` BorderThickness=`new Thickness(0, 1, 0, 0)`>
                <Grid ColumnDefinitions=<>
                    <ColumnDefinition />
                    <ColumnDefinition Width=Auto />
                    <ColumnDefinition Width=Auto />
                </>>
                    <TextBlock Text=`Store.ConnectionStatus` FontSize=11 Foreground=`theme.SecondaryText` TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                    <Button Grid.Column=1 Margin=`new Thickness(8, 0, 0, 0)` Padding=`new Thickness(6, 4, 6, 4)` ToolTipService.ToolTip="Open Folder" Click+=`(sender, e) => OnOpenFolder(sender, e)`>
                        <AppSymbolIcon Symbol=Folder FontSize=11 />
                    </Button>
                    <Button Grid.Column=2 Margin=`new Thickness(8, 0, 0, 0)` Padding=`new Thickness(6, 4, 6, 4)` ToolTipService.ToolTip="New window" Click+=`(sender, e) => OnNewWindow(sender, e)`>
                        <AppSymbolIcon Symbol=NewWindow FontSize=11 />
                    </Button>
                </Grid>
            </Border>
        </Grid>
    </root>
    """)]
public partial class SessionSidebar : IQuickMarkupComponent
{
    /// <summary>Number of sessions shown per directory group before the "Show more" toggle appears.</summary>
    private const int MaxVisibleSessions = 5;

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

    private void OnToggleShowMore(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string directory) return;
        Store.ToggleDirectoryExpanded(directory);
    }

    private void OnOpenInVSCode(object sender, RoutedEventArgs e) => RunFolderAction(sender, FolderLauncher.OpenInVSCode);

    private void OnOpenInFileManager(object sender, RoutedEventArgs e) => RunFolderAction(sender, FolderLauncher.OpenInFileManager);

    /// <summary>Runs a folder-launch action for the clicked group's directory, surfacing failures as a toast.</summary>
    private void RunFolderAction(object sender, Func<string, string?> action)
    {
        if ((sender as Button)?.CommandParameter is not string directory) return;
        var error = action(directory);
        if (error is null) return;
        Store.ShowToast(new ToastItem
        {
            Title = "Open folder",
            Message = error,
            Variant = "error",
        });
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e) => _ = OpenFolderAndStartSessionAsync();

    /// <summary>
    /// Opens a folder picker and starts a new session in the picked folder. The session is
    /// created lazily on the first message send, so no empty server-side session is produced.
    /// </summary>
    private async Task OpenFolderAndStartSessionAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        try
        {
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null) await Store.NewSessionAsync(folder.Path);
        }
        catch (Exception ex)
        {
            Store.ConnectionStatus = $"Folder picker error: {ex.Message}";
        }
    }

    private void OnNewWindow(object sender, RoutedEventArgs e) => UnoVibe.App.CreateWindow();

    private void OnToggleMcp(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string name) return;
        _ = Store.ToggleMcpAsync(name);
    }

    /// <summary>
    /// Expands/collapses the MCP section. Expansion starts the background status poll and
    /// refreshes immediately; collapsing stops the poll (the store's one-shot refresh on
    /// connect/session-switch/toggle still applies).
    /// </summary>
    private void OnToggleMcpExpanded()
    {
        McpExpanded = !McpExpanded;
        Store.SetMcpPolling(McpExpanded);
        if (McpExpanded) _ = Store.RefreshMcpStatusAsync();
    }

    /// <summary>Sidebar status-dot color for an MCP server.</summary>
    private static Brush? McpDot(McpServerItem m) => m.Status switch
    {
        "connected" => ThemeBrushes.Global.SystemSuccess,
        "failed" => ThemeBrushes.Global.SystemCritical,
        "needs_auth" => ThemeBrushes.Global.SystemCaution,
        "needs_client_registration" => ThemeBrushes.Global.SystemCritical,
        _ => ThemeBrushes.Global.TertiaryText,
    };

    /// <summary>Detail line under an MCP server name: status label, plus the error when present.</summary>
    private static string McpStatusDetail(McpServerItem m) =>
        m.Status == "failed" || m.Status == "needs_client_registration"
            ? $"{m.StatusLabel}: {m.Error}"
            : m.StatusLabel;

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
