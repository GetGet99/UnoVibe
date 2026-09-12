using UnoVibe.Models;

namespace UnoVibe.Pages.Main;

[QuickMarkup("""
    using UnoVibe.Services;
    using UnoVibe.Providers;
    using UnoVibe.Models;
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Microsoft.UI;
    bool McpExpanded = false;
    inject bool SettingsOpen;
    inject SessionsSource Sessions;
    inject EventSource Events;
    inject ToastService Toasts;
    inject bool IsCompact;
    inject bool IsSidebarView;
    inject OpencodeClient Opencode;
    inject UIService UIs;
    private McpService McpService = `null!`;
    <Border Grid.Row=1 Padding=`new Thickness(12, 8, 12, 8)` BorderBrush=`theme.DividerStroke` BorderThickness=`new Thickness(0, 1, 0, 0)`>
        <StackPanel Spacing=6>
            <Grid ColumnDefinitions=<>
                <ColumnDefinition />
                <ColumnDefinition Width=Auto />
            </> ColumnSpacing=8>
                mcpToggle = <Button Padding=`new Thickness(4, 2, 4, 2)` HorizontalAlignment=Left Background=`transparent` BorderThickness=0 @Click+=`OnToggleMcpExpanded()` ToolTipService.ToolTip="MCP servers">
                    <StackPanel Orientation=Horizontal Spacing=6>
                        <TextBlock Text=`McpExpanded ? "▼" : "▶"` FontSize=9 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
                        <TextBlock Text="MCP" FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                        <TextBlock Text=`McpService.Si,,ary` FontSize=10 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
                    </StackPanel>
                </Button>
                <Button Grid.Column=1 Padding=`new Thickness(6, 3, 6, 3)` @Click+=`_ = McpService.RefreshMcpStatusAsync()` ToolTipService.ToolTip="Refresh MCP status" Visibility=`McpExpanded ? Visibility.Visible : Visibility.Collapsed`>
                    <AppSymbolIcon Symbol=Refresh FontSize=11 />
                </Button>
            </Grid>
            if (`McpExpanded`)
            {
                <ScrollViewer MaxHeight=200 VerticalScrollBarVisibility=Auto>
                    <StackPanel Spacing=6>
                        foreach (var m in `McpService.Servers`; `m.Name`)
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
                <TextBlock Text=`$"Directory: {McpService.Directory}"` FontSize=10 Foreground=`theme.TertiaryText` TextTrimming=`TextTrimming.CharacterEllipsis` />
            }
        </StackPanel>
    </Border>
    """)]
public partial class McpStatusView : IQuickMarkupComponent
{

    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();
        Sessions.ActiveSessionDirectoryComp.Watch(directory =>
        {
            if (directory == McpService.Directory) return;
            McpService?.Dispose();
            McpService = new(Opencode, Events, Toasts, MarkupNode.DispatcherQueue, directory);
        }, immediete: true);
        // The /mcps built-in command (fired from the chat composer) reveals this section.
        UIs.McpSectionRequested += () => _ = RevealMcpSectionAsync();
    }

    /// <summary>
    /// Reveals the MCP section for the /mcps built-in command: on compact windows the sidebar
    /// itself is hidden, so switch to the sidebar view first; then expand the section (starting
    /// its status poll) and put keyboard focus on the toggle.
    /// </summary>
    private async Task RevealMcpSectionAsync()
    {
        if (IsCompact) IsSidebarView = true;
        if (!McpExpanded) OnToggleMcpExpanded();
        await Task.Delay(16); // let the reactive tree materialize before focusing
        mcpToggle?.Focus(FocusState.Programmatic);
    }

    private void OnToggleMcp(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string name) return;
        _ = McpService.ToggleMcpAsync(name);
    }

    /// <summary>
    /// Expands/collapses the MCP section. Expansion starts the background status poll and
    /// refreshes immediately; collapsing stops the poll (the store's one-shot refresh on
    /// connect/session-switch/toggle still applies).
    /// </summary>
    private void OnToggleMcpExpanded()
    {
        McpExpanded = !McpExpanded;
        McpService.Polling = McpExpanded;
        if (McpExpanded) _ = McpService.RefreshMcpStatusAsync();
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
}
