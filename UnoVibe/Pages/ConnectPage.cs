using UnoVibe;
using UnoVibe.Services;
using UnoVibe.Models;

namespace UnoVibe.Pages;

/// <summary>
/// Shown at startup when no launch-target argument was given, and used as the host for a
/// command-line launch target (folder path or server URL) while it connects. Lets the user
/// either connect to an existing opencode server or launch a local `opencode serve` from
/// a picked folder, then navigates to the main chat page. Recent folders and server
/// URLs are listed VSCode-style; the folder-security toggle on the right is the
/// single source of truth for folder passwords (recent or new).
/// </summary>
[QuickMarkup("""
    using UnoVibe.Services;
    using UnoVibe.Models;
    using UnoVibe.Controls;
    using UnoVibe.Pages;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Microsoft.UI;
    using Windows.UI.Text;
    string Url = "";
    string Status = "Choose a server to connect to.";
    bool Connecting = false;
    string ServerPassword = "";
    bool UseGeneratedPassword = true;
    string CustomPassword = "";
    string ConfirmPassword = "";
    bool SaveFolderPassword = false;
    bool ShowConnectForm = false;
    <setup>
        var theme = ThemeBrushes.Global;
        var transparent = new SolidColorBrush(Colors.Transparent);
    </setup>
    <root>
        <Grid RowDefinitions=<>
            <RowDefinition />
            <RowDefinition Height=Auto />
        </>>
            scrollHost = <ScrollViewer Grid.Row=0 VerticalScrollBarVisibility=Auto>
                content = <StackPanel MaxWidth=880 Padding=`new Thickness(28, 40, 28, 32)` Spacing=16 HorizontalAlignment=Center VerticalAlignment=Center>
                    <StackPanel Spacing=4 HorizontalAlignment=Center>
                        <TextBlock Text="UnoVibe" FontSize=28 FontWeight=`FontWeights.SemiBold` HorizontalAlignment=Center />
                        <TextBlock Text="Connect to OpenCode" FontSize=18 FontWeight=`FontWeights.SemiBold` HorizontalAlignment=Center />
                    </StackPanel>
                    <StackPanel Orientation=Horizontal Spacing=8 HorizontalAlignment=Center>
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

                        <StackPanel Grid.Column=1 Spacing=12>
                            <Border Background=`theme.CardBackground` BorderBrush=`theme.CardStroke` BorderThickness=1 CornerRadius=8 Padding=`new Thickness(16, 14, 16, 14)`>
                                <StackPanel Spacing=10>
                                    <TextBlock Text="Start a session" FontSize=14 FontWeight=`FontWeights.SemiBold` />
                                    <Button HorizontalAlignment=Stretch HorizontalContentAlignment=Left @Click+=`await PickFolderAsync()` IsEnabled=`!Connecting` ToolTipService.ToolTip="Pick a folder to run opencode serve in; it starts with the security settings below">
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
                                            <TextBlock Text="Leave blank if the server has no password. Uses the OPENCODE_SERVER_PASSWORD environment variable when set. Passwords are never stored — reopening a password-protected server asks for it again." FontSize=11 Foreground=`theme.TertiaryText` TextWrapping=Wrap />
                                        </StackPanel>
                                    }
                                    <Border BorderBrush=`theme.DividerStroke` BorderThickness=`new Thickness(0, 1, 0, 0)` Margin=`new Thickness(0, 6, 0, 0)` />
                                    <TextBlock Text="Folder security" FontSize=13 FontWeight=`FontWeights.SemiBold` />
                                    <TextBlock Text="Used when you open any folder — from this list or with Open Folder." FontSize=11 Foreground=`theme.TertiaryText` TextWrapping=Wrap />
                                    <ToggleSwitch Header="Server security" OnContent="Use a generated strong password" OffContent="Set my own password" IsOn<=>`UseGeneratedPassword` IsEnabled=`!Connecting` />
                                    if (`!UseGeneratedPassword`)
                                    {
                                        <PasswordBox Password<=>`CustomPassword` PlaceholderText="Set a password" IsEnabled=`!Connecting` />
                                        <PasswordBox Password<=>`ConfirmPassword` PlaceholderText="Confirm password" IsEnabled=`!Connecting` />
                                        <StackPanel Orientation=Horizontal Spacing=8>
                                            <TextBlock Text=`SaveFolderPassword ? "Password saved on this device." : "Save this password on this device?"` FontSize=11 Foreground=`theme.TertiaryText` TextWrapping=Wrap VerticalAlignment=Center />
                                            <Button Content=`SaveFolderPassword ? "Forget" : "Save"` FontSize=11 Padding=`new Thickness(8, 4, 8, 4)` VerticalAlignment=Center IsEnabled=`!Connecting` Flyout=passwordFlyout = <Flyout Placement=BottomEdgeAlignedRight>
                                                if (`SaveFolderPassword`)
                                                {
                                                    <StackPanel Spacing=8 MaxWidth=260 Padding=4>
                                                        <TextBlock Text="Stop saving this password?" FontSize=13 FontWeight=`FontWeights.SemiBold` TextWrapping=Wrap />
                                                        <TextBlock Text="The password will no longer be stored on this device. You will type it again when you open a folder." FontSize=11 Foreground=`theme.SecondaryText` TextWrapping=Wrap />
                                                        <StackPanel Orientation=Horizontal Spacing=8 HorizontalAlignment=Right>
                                                            <TextBlock Text="Click outside to cancel" FontSize=11 FontStyle=`FontStyle.Italic` Foreground=`theme.TertiaryText` VerticalAlignment=Center />
                                                            <Button Content="Forget it" CornerRadius=6 Padding=`new Thickness(10,  4, 10,  4)` @Click+=`SetSavePassword(false)` />
                                                        </StackPanel>
                                                    </StackPanel>
                                                }
                                                else
                                                {
                                                    <StackPanel Spacing=8 MaxWidth=280 Padding=4>
                                                        <TextBlock Text="Store this password in plain text?" FontSize=13 FontWeight=`FontWeights.SemiBold` TextWrapping=Wrap />
                                                        <TextBlock Text="If saved, the password is stored unencrypted on this computer and could be read by anyone with access to your files. Only save it if you understand this risk." FontSize=11 Foreground=`theme.SecondaryText` TextWrapping=Wrap />
                                                        <StackPanel Orientation=Horizontal Spacing=8 HorizontalAlignment=Right>
                                                            <TextBlock Text="Click outside to cancel" FontSize=11 FontStyle=`FontStyle.Italic` Foreground=`theme.TertiaryText` VerticalAlignment=Center />
                                                            <Button Content="I understand the risk" CornerRadius=6 Padding=`new Thickness(10,  4, 10,  4)` @Click+=`SetSavePassword(true)` />
                                                        </StackPanel>
                                                    </StackPanel>
                                                }
                                            </Flyout> />
                                        </StackPanel>
                                    }
                                </StackPanel>
                            </Border>
                        </StackPanel>
                    </Grid>

                    <TextBlock Text="Tip: launch with a folder path or server URL to open it directly, e.g. `UnoVibe ~/project` or `UnoVibe http://localhost:4096`." FontSize=11 Foreground=`theme.TertiaryText` HorizontalAlignment=Center TextWrapping=Wrap />
                </StackPanel>
            </ScrollViewer>
        </Grid>
    </root>
    """)]
public partial class ConnectPage : Page
{
    /// <summary>Owning window; set by the consumer before Init so it's ready for use.</summary>
    public WindowController Controller { get; set; } = null!;

    /// <summary>
    /// Command-line launch target (set by the consumer before the constructor method runs).
    /// When present, the page immediately runs the folder/serve or server connect flow and
    /// navigates to the main chat page on success — the VSCode-style `UnoVibe /path` open.
    /// </summary>
    public StartupArgs? Startup { get; set; }

    [QuickMarkupConstructor]
    private void Ctor()
    {
        RecentConnectionsStore.Load();
        SettingsStore.Load();
        Init();

        // Restore the persisted folder-security settings (the source of truth for folder passwords).
        // A previously-confirmed custom password also pre-fills the confirm box so the stored value
        // passes the match check when opening a folder without retyping it.
        UseGeneratedPassword = RecentConnectionsStore.UseGeneratedPassword;
        SaveFolderPassword = RecentConnectionsStore.SaveFolderPassword;
        CustomPassword = RecentConnectionsStore.CustomPassword;
        if (SaveFolderPassword && CustomPassword.Length > 0)
            ConfirmPassword = CustomPassword;

        // Keep the centered content tall enough to fill the viewport so it stays vertically centered
        // while still scrolling when the window is small.
        void UpdateContentMinHeight()
        {
            var h = scrollHost.ViewportHeight;
            if (Math.Abs(content.MinHeight - h) > 0.5) content.MinHeight = h;
        }
        scrollHost.ViewChanged += (_, _) => UpdateContentMinHeight();
        scrollHost.SizeChanged += (_, _) => UpdateContentMinHeight();

        // Pre-fill the server password box from the standard environment variable.
        ServerPassword = Environment.GetEnvironmentVariable(OpencodeClient.PasswordEnvVar) ?? "";

        if (Startup is { Kind: not LaunchKind.None })
        {
            var startup = Startup;
            Startup = null;
            _ = RunStartupAsync(startup);
        }
    }

    /// <summary>Connects to an existing server URL and records it in the recent list.
    /// A blank password falls back to the OPENCODE_SERVER_PASSWORD environment variable.</summary>
    private async Task ConnectToUrlAsync() =>
        await ConnectCoreAsync(Url, ServerPassword.Trim() is { Length: > 0 } p ? p : null);

    /// <summary>
    /// Connects to an existing server URL and records it in the recent list on success.
    /// <paramref name="password"/> null → the client falls back to OPENCODE_SERVER_PASSWORD;
    /// "" → connect without a password; non-empty → use it.
    /// </summary>
    private async Task ConnectCoreAsync(string url, string? password)
    {
        var clean = url.Trim();
        if (clean.Length == 0) clean = "http://localhost:4096";
        if (!clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            clean = "http://" + clean;

        Connecting = true;
        Status = $"Connecting to {clean}...";
        var store = Controller.Store;
        store.Configure(clean, password);
        store.DisplayLabel = clean;
        await store.ConnectAsync();
        Connecting = false;

        if (store.ConnectionStatus == "Connected")
        {
            // Never persist the password itself — only record that the server needs one,
            // so a later click on the recent entry can prompt for it.
            RecentConnectionsStore.UpsertServer(clean, password is { Length: > 0 });
            Controller.ShowMain();
        }
        else
        {
            Status = store.ConnectionStatus;
        }
    }

    /// <summary>
    /// Resolves the folder password from the UI security settings (the source of truth):
    /// generated strong password, or a validated custom password. Returns Ok=false (with a
    /// status message) when a custom password is missing or doesn't match its confirmation.
    /// </summary>
    private (bool Ok, string? Password) ResolveUiFolderPassword()
    {
        if (UseGeneratedPassword) return (true, null);
        if (CustomPassword.Length == 0)
        {
            Status = "Please set a password.";
            return (false, null);
        }
        if (CustomPassword != ConfirmPassword)
        {
            Status = "Passwords do not match.";
            return (false, null);
        }
        return (true, CustomPassword);
    }

    /// <summary>
    /// Picks a folder and immediately launches `opencode serve` there using the current
    /// folder-security settings (the source of truth), saving a click.
    /// </summary>
    private async Task PickFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        WindowsHelper.InitializeWithWindow(picker, Controller.Window);
        try
        {
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;
            var (ok, password) = ResolveUiFolderPassword();
            if (!ok) return;
            if (await StartServeCoreAsync(folder.Path, password))
            {
                RecentConnectionsStore.SaveSecurity(UseGeneratedPassword, SaveFolderPassword, CustomPassword);
                Controller.ShowMain();
            }
        }
        catch (Exception ex)
        {
            Status = $"Folder picker error: {ex.Message}";
        }
    }

    /// <summary>
    /// Launches a local `opencode serve` in <paramref name="folder"/> and connects.
    /// <paramref name="password"/> null → generate a strong password; "" → no password;
    /// non-empty → use it. On success the folder is recorded in the recent list.
    /// Returns true when connected.
    /// </summary>
    private async Task<bool> StartServeCoreAsync(string folder, string? password)
    {
        Connecting = true;
        Status = "Starting opencode serve...";

        var serve = new ServeProcess(password);
        var result = await serve.StartAsync(folder);
        if (!result.StartsWith("http://"))
        {
            serve.Dispose();
            Status = result;
            Connecting = false;
            return false;
        }

        Status = $"Server ready at {result}";
        var store = Controller.Store;
        store.AttachServeProcess(serve);
        store.Configure(result, serve.Password);
        await store.ConnectAsync();
        Connecting = false;

        if (store.ConnectionStatus != "Connected")
        {
            Status = store.ConnectionStatus;
            return false;
        }

        RecentConnectionsStore.UpsertFolder(folder);
        return true;
    }

    /// <summary>
    /// Runs a command-line launch target: a folder starts `opencode serve` there (the folder
    /// is created if missing) and a server URL connects directly. On success the main chat
    /// page is shown; on failure the ConnectPage stays with the error in its status line.
    /// </summary>
    private async Task RunStartupAsync(StartupArgs startup)
    {
        switch (startup.Kind)
        {
            case LaunchKind.Folder:
                if (await StartServeCoreAsync(startup.Value, startup.ResolveFolderPassword()))
                    Controller.ShowMain();
                break;
            case LaunchKind.Server:
                await ConnectCoreAsync(startup.Value, startup.ResolveServerPassword());
                break;
        }
    }

    /// <summary>
    /// Re-opens a recent entry: folder → serve using the current folder-security settings; server → direct connect.
    /// </summary>
    private async Task OnOpenRecent(object sender, RoutedEventArgs e)
    {
        if (Connecting || (sender as Button)?.CommandParameter is not RecentConnection item) return;
        if (item.IsFolder)
        {
            var (ok, password) = ResolveUiFolderPassword();
            if (!ok) return;
            if (await StartServeCoreAsync(item.Detail, password))
            {
                RecentConnectionsStore.SaveSecurity(UseGeneratedPassword, SaveFolderPassword, CustomPassword);
                Controller.ShowMain();
            }
        }
        else
        {
            // A password-protected server's password is never stored — ask for it on reopen.
            string? password = null;
            if (item.RequiresPassword)
            {
                password = await PromptForServerPasswordAsync(item.Detail);
                if (password is null) return; // cancelled
                if (password.Length == 0) password = null;
            }
            await ConnectCoreAsync(item.Detail, password);
        }
    }

    private void OnRemoveRecent(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is not string key) return;
        RecentConnectionsStore.Remove(key);
    }

    /// <summary>
    /// Opt-in/out of persisting the custom folder password. Enabling requires confirming the
    /// plain-text-storage risk in the flyout; the choice is saved immediately either way.
    /// </summary>
    private void SetSavePassword(bool save)
    {
        if (passwordFlyout is { IsOpen: true }) passwordFlyout.Hide();
        SaveFolderPassword = save;
        RecentConnectionsStore.SaveSecurity(UseGeneratedPassword, save, CustomPassword);
    }

    /// <summary>
    /// Asks the user for the password of a password-protected server URL that has no stored
    /// password (only the <c>RequiresPassword</c> flag). Returns the entered password, an empty
    /// string when the user wants to try without one, or null when the dialog is cancelled.
    /// </summary>
    private async Task<string?> PromptForServerPasswordAsync(string url)
    {
        var box = new PasswordBox { PlaceholderText = "Server password", Width = 300 };
        var dialog = new ContentDialog
        {
            Title = "Password required",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = $"Enter the password for {url}", TextWrapping = TextWrapping.Wrap },
                    box,
                },
            },
            PrimaryButtonText = "Connect",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return null;
        return box.Password;
    }
}
