using UnoVibe.Services;
using UnoVibe.Models;
using Windows.ApplicationModel.DataTransfer;

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
    inject Window HostWindow;
    inject bool SettingsOpen;
    bool McpExpanded = false;
    bool ShowPassword = false;
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
                            </> ColumnSpacing=4>
                                <StackPanel Orientation=Horizontal Spacing=4>
                                    <TextBlock Text=`DisplayPath(group.Directory)` FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.SecondaryText` TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                                    if (`group.Branch.Length > 0`)
                                    {
                                        <TextBlock Text=`$"⎇ {group.Branch}"` FontSize=10 Foreground=`theme.TertiaryText` TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                                    }
                                </StackPanel>
                                // TODO: When attached property support falling back to element properly, do that instead of wrapping in Grid.
                                <Grid Grid.Column=1 VerticalAlignment=Center>
                                    <FolderActions Directory=`group.Directory` />
                                </Grid>
                            </Grid>
                            if (`group.Sessions.Reactive.Count == 0`)
                            {
                                <TextBlock Text="No sessions yet" FontSize=11 Foreground=`theme.TertiaryText` Margin=`new Thickness(0, 6, 0, 0)` />
                            }
                            foreach (var s in `group.IsExpanded ? group.Sessions.Reactive : group.Sessions.Reactive.Take(MaxVisibleSessions)`; `s.Id`)
                            {
                                <Button Margin=`new Thickness(0, 4, 0, 0)` Padding=`new Thickness(8, 6, 8, 6)` HorizontalAlignment=Stretch HorizontalContentAlignment=Left CommandParameter=`s.Id` Click+=`(sender, e) => OnSwitchSession(sender, e)` Background=`Store.ActiveSessionId == s.Id ? theme.ControlFill : transparent` ContextFlyout=sessionMenu = <MenuFlyout Placement=BottomEdgeAlignedRight>
                                        if (`s.IsRead`) {
                                            <MenuFlyoutItem Text="Mark as unread" CommandParameter=`s.Id` Click+=`(sender, e) => OnMarkUnread(sender, e)` />
                                        } else {
                                            <MenuFlyoutItem Text="Mark as read" CommandParameter=`s.Id` Click+=`(sender, e) => OnMarkRead(sender, e)` />
                                        }
                                    </MenuFlyout>>
                                    <Grid ColumnDefinitions=<>
                                        <ColumnDefinition Width=Auto />
                                        <ColumnDefinition />
                                        <ColumnDefinition Width=Auto />
                                    </>>
                                        <Grid Width=14 Margin=`new Thickness(0, 0, 6, 0)` VerticalAlignment=Center>
                                            <AppSymbolIcon Symbol=`AttentionSymbol(s)` FontSize=10 Foreground=`theme.SystemAttention` Visibility=`s.NeedsAttention ? Visibility.Visible : Visibility.Collapsed` HorizontalAlignment=Center VerticalAlignment=Center />
                                            <ProgressRing Width=12 Height=12 IsActive=`s.IsBusy` Visibility=`!s.NeedsAttention && s.IsBusy ? Visibility.Visible : Visibility.Collapsed` HorizontalAlignment=Center VerticalAlignment=Center />
                                            <AppSymbolIcon Symbol=`OutcomeSymbol(s)` FontSize=10 Foreground=`OutcomeBrush(s)` Visibility=`!s.NeedsAttention && s.IsUnread && !s.IsRead && !s.IsBusy && s.Outcome.Length > 0 ? Visibility.Visible : Visibility.Collapsed` HorizontalAlignment=Center VerticalAlignment=Center />
                                            <Border Width=6 Height=6 CornerRadius=`new CornerRadius(3)` Background=`theme.SystemAttention` Visibility=`!s.NeedsAttention && s.IsUnread && !s.IsRead && !s.IsBusy && s.Outcome.Length == 0 ? Visibility.Visible : Visibility.Collapsed` HorizontalAlignment=Center VerticalAlignment=Center />
                                            <AppSymbolIcon Symbol=Message FontSize=10 Foreground=`theme.TertiaryText` Visibility=`!s.NeedsAttention && !s.IsBusy && (s.IsRead || !s.IsUnread) ? Visibility.Visible : Visibility.Collapsed` HorizontalAlignment=Center VerticalAlignment=Center />
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
                    <Button Grid.Column=3 Margin=`new Thickness(8, 0, 0, 0)` Padding=`new Thickness(6, 4, 6, 4)` ToolTipService.ToolTip="Settings" @Click+=`SettingsOpen = true`>
                        <AppSymbolIcon Symbol=Setting FontSize=11 />
                    </Button>
                    <Button Grid.Column=4 Margin=`new Thickness(8, 0, 0, 0)` Padding=`new Thickness(6, 4, 6, 4)` ToolTipService.ToolTip="Connection details" Flyout=connectionFlyout = <Flyout Placement=Top @Closed+=`ShowPassword = false`>
                        <StackPanel Spacing=10 MinWidth=320 MaxWidth=400>
                            <TextBlock Text="Connection" FontSize=13 FontWeight=`FontWeights.SemiBold` />
                            <Grid ColumnSpacing=8 ColumnDefinitions=<>
                                <ColumnDefinition Width=Auto />
                                <ColumnDefinition />
                                <ColumnDefinition Width=Auto />
                            </>>
                                <TextBlock Text="Directory" FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                                <TextBlock Grid.Column=1 Text=`Store.ServerDirectory` FontSize=12 IsTextSelectionEnabled=true TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center ToolTipService.ToolTip=`Store.ServerDirectory` />
                                <Button Grid.Column=2 Padding=`new Thickness(6, 3, 6, 3)` ToolTipService.ToolTip="Copy directory" @Click+=`CopyToClipboard("Directory", Store.ServerDirectory)`>
                                    <AppSymbolIcon Symbol=Copy FontSize=11 />
                                </Button>
                            </Grid>
                            <Grid ColumnSpacing=8 ColumnDefinitions=<>
                                <ColumnDefinition Width=Auto />
                                <ColumnDefinition />
                                <ColumnDefinition Width=Auto />
                            </>>
                                <TextBlock Text="Server" FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                                <TextBlock Grid.Column=1 Text=`Store.ConnectionUrl` FontSize=12 IsTextSelectionEnabled=true TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center ToolTipService.ToolTip=`Store.ConnectionUrl` />
                                <Button Grid.Column=2 Padding=`new Thickness(6, 3, 6, 3)` ToolTipService.ToolTip="Copy URL" @Click+=`CopyToClipboard("URL", Store.ConnectionUrl)`>
                                    <AppSymbolIcon Symbol=Copy FontSize=11 />
                                </Button>
                            </Grid>
                            <Grid ColumnSpacing=8 ColumnDefinitions=<>
                                <ColumnDefinition Width=Auto />
                                <ColumnDefinition />
                                <ColumnDefinition Width=Auto />
                                <ColumnDefinition Width=Auto />
                            </>>
                                <TextBlock Text="Password" FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                                <TextBlock Grid.Column=1 Text=`ShowPassword ? Store.ConnectionPassword : MaskPassword(Store.ConnectionPassword)` FontSize=12 IsTextSelectionEnabled=true TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center ToolTipService.ToolTip=`ShowPassword ? Store.ConnectionPassword : "Hidden — click the eye to reveal"` />
                                <Button Grid.Column=2 Padding=`new Thickness(6, 3, 6, 3)` Visibility=`Store.ConnectionPassword.Length > 0 ? Visibility.Visible : Visibility.Collapsed` ToolTipService.ToolTip=`ShowPassword ? "Hide password" : "Show password"` @Click+=`ShowPassword = !ShowPassword`>
                                    <AppSymbolIcon Symbol=View FontSize=11 />
                                </Button>
                                <Button Grid.Column=3 Padding=`new Thickness(6, 3, 6, 3)` Visibility=`Store.ConnectionPassword.Length > 0 ? Visibility.Visible : Visibility.Collapsed` ToolTipService.ToolTip="Copy password" @Click+=`CopyToClipboard("Password", Store.ConnectionPassword)`>
                                    <AppSymbolIcon Symbol=Copy FontSize=11 />
                                </Button>
                            </Grid>
                        </StackPanel>
                    </Flyout>>
                        <AppSymbolIcon Symbol=More FontSize=11 />
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

    /// <summary>
    /// Right-click context-menu actions: "Mark as unread" / "Mark as read". The session id is
    /// carried by the <see cref="MenuFlyoutItem.CommandParameter"/> and resolved from the sender.
    /// </summary>
    private void OnMarkUnread(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuFlyoutItem)?.CommandParameter is not string id) return;
        Store.SetSessionRead(id, read: false);
    }

    private void OnMarkRead(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuFlyoutItem)?.CommandParameter is not string id) return;
        Store.SetSessionRead(id, read: true);
    }

    private void OnToggleShowMore(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string directory) return;
        Store.ToggleDirectoryExpanded(directory);
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e) => _ = OpenFolderAndStartSessionAsync();

    /// <summary>
    /// Opens a folder picker and starts a new session in the picked folder. The session is
    /// created lazily on the first message send, so no empty server-side session is produced.
    /// </summary>
    private async Task OpenFolderAndStartSessionAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        WindowsHelper.InitializeWithWindow(picker, HostWindow);
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

    /// <summary>
    /// Renders the connection password while hidden: a fixed-width bullet mask, or "None"
    /// when the server has no password. The real value is never shown by default.
    /// </summary>
    private static string MaskPassword(string password) =>
        password.Length == 0 ? "None" : "••••••••";

    /// <summary>Copies a connection value to the system clipboard and confirms with a toast.</summary>
    private void CopyToClipboard(string label, string text)
    {
        var data = new DataPackage();
        data.SetText(text);
        Clipboard.SetContent(data);
        Store.ShowToast(new ToastItem
        {
            Title = "Copied",
            Message = $"{label} copied to clipboard.",
            Variant = "success",
        });
    }

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

    /// <summary>
    /// Path relative to the connected server's directory via <see cref="PathDisplay.Relative"/>.
    /// </summary>
    private string DisplayPath(string fullPath) => PathDisplay.Relative(fullPath, Store.ServerDirectory);
}
