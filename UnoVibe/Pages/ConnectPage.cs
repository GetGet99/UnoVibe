using UnoVibe;
using UnoVibe.Services;
using UnoVibe.Models;

namespace UnoVibe.Pages;

/// <summary>
/// Shown at startup when no OPENCODE_BASE_URL was provided. Lets the user either
/// connect to an existing opencode server or launch a local `opencode serve` from
/// a picked folder, then navigates to the main chat page. Recent folders and server
/// URLs are listed VSCode-style; folder password settings are remembered per entry.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Services;
    using UnoVibe.Models;
    using UnoVibe.Controls;
    using UnoVibe.Pages;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Microsoft.UI;
    string Url = "";
    string Status = "Choose a server to connect to.";
    string Folder = "";
    bool Connecting = false;
    string ServerPassword = "";
    bool UseGeneratedPassword = true;
    string CustomPassword = "";
    string ConfirmPassword = "";
    bool ShowConnectForm = false;
    <setup>
        var theme = ThemeBrushes.Global;
        var transparent = new SolidColorBrush(Colors.Transparent);
    </setup>
    <root>
        <Grid RowDefinitions=<>
            <RowDefinition Height=Auto />
            <RowDefinition />
            <RowDefinition Height=Auto />
        </>>
            <TextBlock Grid.Row=0 Text="UnoVibe" FontSize=28 FontWeight=`FontWeights.SemiBold` Padding=`new Thickness(28, 24, 28, 0)` />
            <ScrollViewer Grid.Row=1>
                <StackPanel Padding=`new Thickness(28, 16, 28, 24)` Spacing=16 MaxWidth=860 HorizontalAlignment=Left>
                    <TextBlock Text="Connect to OpenCode" FontSize=18 FontWeight=`FontWeights.SemiBold` />
                    <StackPanel Orientation=Horizontal Spacing=8>
                        <ProgressRing Width=14 Height=14 IsActive=`Connecting` Visibility=`Connecting ? Visibility.Visible : Visibility.Collapsed` VerticalAlignment=Center />
                        <TextBlock Text=`Status` FontSize=12 Foreground=`theme.SecondaryText` TextWrapping=Wrap IsTextSelectionEnabled=true VerticalAlignment=Center />
                    </StackPanel>

                    <Grid ColumnDefinitions=<>
                        <ColumnDefinition Width=`new GridLength(1.4, GridUnitType.Star)` />
                        <ColumnDefinition />
                    </> ColumnSpacing=16>
                        <Border Grid.Column=0 Background=`theme.CardBackground` BorderBrush=`theme.CardStroke` BorderThickness=1 CornerRadius=8 Padding=`new Thickness(16, 14, 16, 14)`>
                            <StackPanel Spacing=10>
                                <Grid ColumnDefinitions=<>
                                    <ColumnDefinition />
                                    <ColumnDefinition Width=Auto />
                                </> ColumnSpacing=8>
                                    <TextBlock Text="Recent" FontSize=14 FontWeight=`FontWeights.SemiBold` VerticalAlignment=Center />
                                    <Button Grid.Column=1 Content="Clear all" FontSize=11 Padding=`new Thickness(6, 3, 6, 3)` Background=`transparent` BorderThickness=0 Visibility=`RecentConnectionsStore.Items.Reactive.Count > 0 ? Visibility.Visible : Visibility.Collapsed` @Click+=`RecentConnectionsStore.ClearAll()` ToolTipService.ToolTip="Remove all recent entries" />
                                </Grid>
                                if (`RecentConnectionsStore.Items.Reactive.Count == 0`)
                                {
                                    <TextBlock Text="No recent connections yet. Open a folder or connect to a URL to get started." FontSize=12 Foreground=`theme.TertiaryText` TextWrapping=Wrap />
                                }
                                else
                                {
                                    <ScrollViewer MaxHeight=320>
                                        <StackPanel Spacing=2>
                                            foreach (var item in `RecentConnectionsStore.Items`; `item.Key`)
                                            {
                                                <Grid ColumnDefinitions=<>
                                                    <ColumnDefinition Width=Auto />
                                                    <ColumnDefinition />
                                                    <ColumnDefinition Width=Auto />
                                                </> ColumnSpacing=10>
                                                    <AppSymbolIcon Symbol=`item.IsFolder ? Symbol.Folder : Symbol.Globe` FontSize=14 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                                                    <Button Grid.Column=1 HorizontalAlignment=Stretch HorizontalContentAlignment=Left Background=`transparent` BorderThickness=0 Padding=`new Thickness(0, 6, 0, 6)` CommandParameter=`item` Click+=`(sender, e) => OnOpenRecent(sender, e)` ToolTipService.ToolTip=`RecentTooltip(item)` IsEnabled=`!Connecting`>
                                                        <StackPanel Spacing=1>
                                                            <TextBlock Text=`item.Display` FontSize=13 FontWeight=`FontWeights.SemiBold` TextTrimming=`TextTrimming.CharacterEllipsis` />
                                                            <TextBlock Text=`RecentDetail(item)` FontSize=11 Foreground=`theme.TertiaryText` TextTrimming=`TextTrimming.CharacterEllipsis` />
                                                        </StackPanel>
                                                    </Button>
                                                    <Button Grid.Column=2 Padding=`new Thickness(8, 4, 8, 4)` VerticalAlignment=Center Background=`transparent` BorderThickness=0 CommandParameter=`item.Key` Click+=`(sender, e) => OnRemoveRecent(sender, e)` ToolTipService.ToolTip="Remove from recent" IsEnabled=`!Connecting`>
                                                        <AppSymbolIcon Symbol=Cancel FontSize=10 Foreground=`theme.TertiaryText` />
                                                    </Button>
                                                </Grid>
                                            }
                                        </StackPanel>
                                    </ScrollViewer>
                                }
                            </StackPanel>
                        </Border>

                        <StackPanel Grid.Column=1 Spacing=12>
                            <Border Background=`theme.CardBackground` BorderBrush=`theme.CardStroke` BorderThickness=1 CornerRadius=8 Padding=`new Thickness(16, 14, 16, 14)`>
                                <StackPanel Spacing=10>
                                    <TextBlock Text="Start a session" FontSize=14 FontWeight=`FontWeights.SemiBold` />
                                    <Button HorizontalAlignment=Stretch HorizontalContentAlignment=Left @Click+=`await PickFolderAsync()` IsEnabled=`!Connecting` ToolTipService.ToolTip="Run opencode serve in a project folder">
                                        <StackPanel Orientation=Horizontal Spacing=8>
                                            <AppSymbolIcon Symbol=Folder FontSize=14 />
                                            <TextBlock Text="Open Folder" VerticalAlignment=Center />
                                        </StackPanel>
                                    </Button>
                                    <Button HorizontalAlignment=Stretch HorizontalContentAlignment=Left @Click+=`ShowConnectForm = !ShowConnectForm` IsEnabled=`!Connecting` ToolTipService.ToolTip="Connect to an existing opencode server">
                                        <StackPanel Orientation=Horizontal Spacing=8>
                                            <AppSymbolIcon Symbol=Globe FontSize=14 />
                                            <TextBlock Text="Connect to URL" VerticalAlignment=Center />
                                        </StackPanel>
                                    </Button>
                                    if (`ShowConnectForm`)
                                    {
                                        <StackPanel Spacing=8>
                                            <TextBox Text<=>`Url` PlaceholderText="http://localhost:4096" IsEnabled=`!Connecting` />
                                            <PasswordBox Password<=>`ServerPassword` PlaceholderText="Server password (optional)" IsEnabled=`!Connecting` />
                                            <Button Content="Connect" @Click+=`await ConnectToUrlAsync()` IsEnabled=`!Connecting` HorizontalAlignment=Right />
                                            <TextBlock Text="Leave blank if the server has no password. Uses the OPENCODE_SERVER_PASSWORD environment variable when set." FontSize=11 Foreground=`theme.TertiaryText` TextWrapping=Wrap />
                                        </StackPanel>
                                    }
                                    <Border BorderBrush=`theme.DividerStroke` BorderThickness=`new Thickness(0, 1, 0, 0)` Margin=`new Thickness(0, 6, 0, 0)` />
                                    <TextBlock Text="New folder security" FontSize=13 FontWeight=`FontWeights.SemiBold` />
                                    <TextBlock Text="Applies when you start a folder. Saved with that folder so re-opening it later uses the same settings." FontSize=11 Foreground=`theme.TertiaryText` TextWrapping=Wrap />
                                    <ToggleSwitch Header="Server security" OnContent="Use a generated strong password" OffContent="Set my own password" IsOn<=>`UseGeneratedPassword` IsEnabled=`!Connecting` />
                                    if (`!UseGeneratedPassword`)
                                    {
                                        <PasswordBox Password<=>`CustomPassword` PlaceholderText="Set a password" IsEnabled=`!Connecting` />
                                        <PasswordBox Password<=>`ConfirmPassword` PlaceholderText="Confirm password" IsEnabled=`!Connecting` />
                                    }
                                    <Grid ColumnDefinitions=<>
                                        <ColumnDefinition />
                                        <ColumnDefinition Width=Auto />
                                    </> ColumnSpacing=8>
                                        <TextBlock Text=`Folder.Length > 0 ? Folder : "(no folder selected)"` FontSize=12 Foreground=`Folder.Length > 0 ? theme.PrimaryText : theme.TertiaryText` TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center ToolTipService.ToolTip=`Folder.Length > 0 ? Folder : ""` />
                                        <Button Grid.Column=1 Content="Start & connect" @Click+=`await StartServeAsync()` IsEnabled=`!Connecting && Folder.Length > 0` />
                                    </Grid>
                                </StackPanel>
                            </Border>
                        </StackPanel>
                    </Grid>
                </StackPanel>
            </ScrollViewer>
            <TextBlock Grid.Row=2 Text="Tip: set the OPENCODE_BASE_URL environment variable to skip this screen." FontSize=11 Foreground=`theme.TertiaryText` Padding=`new Thickness(28, 0, 28, 16)` />
        </Grid>
    </root>
    """)]
public partial class ConnectPage : Page
{
    /// <summary>Owning window; set by the consumer before Init so it's ready for use.</summary>
    public WindowController Controller { get; set; } = null!;

    [QuickMarkupConstructor]
    private void Ctor()
    {
        RecentConnectionsStore.Load();
        Init();
        var configured = Environment.GetEnvironmentVariable("OPENCODE_BASE_URL");
        if (!string.IsNullOrWhiteSpace(configured)) Url = configured;
        ServerPassword = Environment.GetEnvironmentVariable(OpencodeClient.PasswordEnvVar) ?? "";
    }

    /// <summary>Connects to an existing server URL and records it in the recent list.</summary>
    private async Task ConnectToUrlAsync() => await ConnectCoreAsync(Url, ServerPassword);

    private async Task ConnectCoreAsync(string url, string password)
    {
        var clean = url.Trim();
        if (clean.Length == 0) clean = "http://localhost:4096";
        if (!clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            clean = "http://" + clean;

        Connecting = true;
        Status = $"Connecting to {clean}...";
        var store = Controller.Store;
        var pwd = password.Trim();
        store.Configure(clean, pwd.Length > 0 ? pwd : null);
        await store.ConnectAsync();
        Connecting = false;

        if (store.ConnectionStatus == "Connected")
        {
            RecentConnectionsStore.UpsertServer(clean, pwd);
            Controller.ShowMain();
        }
        else
        {
            Status = store.ConnectionStatus;
        }
    }

    private async Task PickFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        try
        {
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null) Folder = folder.Path;
        }
        catch (Exception ex)
        {
            Status = $"Folder picker error: {ex.Message}";
        }
    }

    /// <summary>Starts `opencode serve` for a newly picked folder using the security card settings.</summary>
    private async Task StartServeAsync()
    {
        if (Folder.Length == 0) return;
        await StartServeCoreAsync(Folder, UseGeneratedPassword, CustomPassword);
    }

    /// <summary>
    /// Launches a local `opencode serve` in <paramref name="folder"/> and connects. On success the
    /// folder (with its password settings) is recorded in the recent list.
    /// </summary>
    private async Task StartServeCoreAsync(string folder, bool useGenerated, string customPassword)
    {
        string? password = null;
        if (!useGenerated)
        {
            if (customPassword.Length == 0)
            {
                Status = "Please set a password.";
                return;
            }
            password = customPassword;
        }

        Connecting = true;
        Status = "Starting opencode serve...";

        var serve = new ServeProcess(password);
        var result = await serve.StartAsync(folder);
        if (!result.StartsWith("http://"))
        {
            serve.Dispose();
            Status = result;
            Connecting = false;
            return;
        }

        Status = $"Server ready at {result}";
        var store = Controller.Store;
        store.AttachServeProcess(serve);
        store.Configure(result, serve.Password);
        await store.ConnectAsync();
        Connecting = false;

        if (store.ConnectionStatus == "Connected")
        {
            RecentConnectionsStore.UpsertFolder(folder, useGenerated, customPassword);
            Controller.ShowMain();
        }
        else
        {
            Status = store.ConnectionStatus;
        }
    }

    /// <summary>Re-opens a recent entry: folder → serve with its saved password settings; server → direct connect.</summary>
    private async Task OnOpenRecent(object sender, RoutedEventArgs e)
    {
        if (Connecting || (sender as Button)?.CommandParameter is not RecentConnection item) return;
        if (item.IsFolder)
            await StartServeCoreAsync(item.Detail, item.UseGeneratedPassword, item.CustomPassword);
        else
            await ConnectCoreAsync(item.Detail, item.ServerPassword);
    }

    private void OnRemoveRecent(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string key) return;
        RecentConnectionsStore.Remove(key);
    }

    /// <summary>Detail line for a recent row; folder rows note when a custom password is saved.</summary>
    private static string RecentDetail(RecentConnection item) =>
        item.IsFolder && !item.UseGeneratedPassword
            ? $"{item.Detail} · custom password"
            : item.Detail;

    private static string RecentTooltip(RecentConnection item) =>
        item.IsFolder
            ? item.UseGeneratedPassword
                ? "Open this folder with a generated password"
                : "Open this folder with the saved custom password"
            : "Connect to this server";
}
