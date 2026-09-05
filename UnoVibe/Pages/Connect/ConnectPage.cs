using UnoVibe;
using UnoVibe.Services;
using UnoVibe.Models;
using UnoVibe.Integration;

namespace UnoVibe.Pages.Connect;

/// <summary>
/// Shown at startup when no launch-target argument was given, and used as the host for a
/// command-line launch target (folder path or server URL) while it connects. Lets the user
/// either connect to an existing opencode server or launch a local `opencode serve` from
/// a picked folder, then navigates to the main chat page. Recent folders and server
/// URLs are listed VSCode-style (<see cref="RecentListPanel"/>); the folder-security
/// toggle on the right (<see cref="ConnectPanel"/>) is the single source of truth for
/// folder passwords (recent or new). The shared form/security state is provided so the
/// panels stay bidirectionally in sync with the page.
/// </summary>
[QuickMarkup("""
    using QuickMarkup.WinUI;
    using UnoVibe.Services;
    provide bool Connecting = false;
    provide bool ShowConnectForm = false;
    provide string Url = "";
    provide string ServerPassword = "";
    provide bool UseGeneratedPassword = true;
    provide string CustomPassword = "";
    provide string ConfirmPassword = "";
    provide bool SaveFolderPassword = false;
    string Status = "Choose a server to connect to.";
    bool IsCompact = false;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <Page>
        <Grid RowDefinitions=<>
            <RowDefinition />
            <RowDefinition Height=Auto />
        </>>
            scrollHost = <ScrollViewer Grid.Row=0 VerticalScrollBarVisibility=Auto>
                content = <StackPanel MaxWidth=880 Padding=`IsCompact ? new Thickness(16, 24, 16, 24) : new Thickness(28, 40, 28, 32)` Spacing=16 HorizontalAlignment=Center VerticalAlignment=Center>
                    <StackPanel Spacing=4 HorizontalAlignment=Center>
                        <TextBlock Text="UnoVibe" FontSize=28 FontWeight=`FontWeights.SemiBold` HorizontalAlignment=Center />
                        <TextBlock Text="Connect to OpenCode" FontSize=18 FontWeight=`FontWeights.SemiBold` HorizontalAlignment=Center />
                    </StackPanel>
                    <WrapPanel HorizontalAlignment=Center>
                        <ProgressRing Width=14 Height=14 IsActive=`Connecting` Visibility=`Connecting ? Visibility.Visible : Visibility.Collapsed` VerticalAlignment=Center Margin=`new Thickness(0, 0, 8, 0)` />
                        <TextBlock Text=`Status` FontSize=12 Foreground=`theme.SecondaryText` TextWrapping=Wrap IsTextSelectionEnabled=true VerticalAlignment=Center />
                    </WrapPanel>

                    <Grid RowDefinitions=<>
                        <RowDefinition />
                        if (`IsCompact`) <RowDefinition Height=`GridLength.Auto` />
                    </> ColumnDefinitions=<>
                        <ColumnDefinition Width=`new GridLength(1.4, GridUnitType.Star)` />
                        if (`!IsCompact`) <ColumnDefinition Width=`new GridLength(1, GridUnitType.Star)` />
                    </> ColumnSpacing=16 RowSpacing=`IsCompact ? 16 : 0`>
                        <Grid Grid.Row=0 Grid.Column=0>
                            <RecentListPanel OpenRecentRequested+=`OnOpenRecent` RemoveRecentRequested+=`OnRemoveRecent` />
                        </Grid>
                        <Grid Grid.Row=`IsCompact ? 1 : 0` Grid.Column=`IsCompact ? 0 : 1`>
                            <ConnectPanel OpenFolderRequested+=`PickFolderAsync` ConnectToUrlRequested+=`ConnectToUrlAsync` />
                        </Grid>
                    </Grid>
                    // we don't have CLI command installed for user yet.
                    // <TextBlock Text="Tip: launch with a folder path or server URL to open it directly, e.g. `unovibe ~/project` or `unovibe http://localhost:4096`." FontSize=11 Foreground=`theme.TertiaryText` HorizontalAlignment=Center TextWrapping=Wrap />

                    await `OpencodeServeProcess.GetExecutableStatus()`
                    with {
                        <TextBlock Text="Checking OpenCode executable status..." FontSize=11 Foreground=`theme.TertiaryText` HorizontalAlignment=Center TextWrapping=Wrap />
                    }
                    catch (err) {
                        <TextBlock Text=`$"Error while checking OpenCode executable status {err}"` FontSize=11 Foreground=`theme.TertiaryText` HorizontalAlignment=Center TextWrapping=Wrap />
                    }
                    then (result) {
                        if (`result is OpencodeExecutableStatus.NotAvaliable`) {
                            <TextBlock Text="OpenCode CLI is not avaliable or not installed in PATH.\nYou can still use UnoVibe to connect to hosted OpenCode server." FontSize=11 Foreground=`theme.TertiaryText` HorizontalAlignment=Center TextWrapping=Wrap TextAlignment=Center />
                            <HyperlinkButton Content="Visit OpenCode Installation Guide" NavigateUri=`new Uri("https://github.com/anomalyco/opencode#installation")` HorizontalAlignment=Center />
                            <TextBlock Text="Please relaunch UnoVibe after completed installation of CLI version." FontSize=11 Foreground=`theme.TertiaryText` HorizontalAlignment=Center TextWrapping=Wrap />
                        } else if (`result is OpencodeExecutableStatus.MayNeedUpgrade`) {
                            <TextBlock Text=`"Installed OpenCode may not be supported. This version of UnoVibe is tested with OpenCode {OpencodeServeProcess.RequiredOpencodeVersion}.\nYou can still use UnoVibe to connect to hosted OpenCode server."` FontSize=11 Foreground=`theme.TertiaryText` HorizontalAlignment=Center TextWrapping=Wrap />
                        }
                    }
                    <TextBlock Text="UnoVibe is not affiliated with OpenCode and is not built by OpenCode team." FontSize=11 Foreground=`theme.TertiaryText` HorizontalAlignment=Center TextWrapping=Wrap />
                    <TextBlock Text="Model provider registration and other configuration must be done in OpenCode CLI and config. See your provider's details for how they handle your data." FontSize=11 Foreground=`theme.TertiaryText` HorizontalAlignment=Center TextWrapping=Wrap />
                    <StackPanel Orientation=Horizontal Spacing=4 HorizontalAlignment=Center>
                        <HyperlinkButton Content="Terms of Use" NavigateUri=`new Uri("https://github.com/GetGet99/UnoVibe/blob/main/TERMS.md")` HorizontalAlignment=Center />
                        <HyperlinkButton Content="Privacy Policy" NavigateUri=`new Uri("https://github.com/GetGet99/UnoVibe/blob/main/PRIVACY.md")` HorizontalAlignment=Center />
                    </StackPanel>
                </StackPanel>
            </ScrollViewer>
        </Grid>
    </Page>
    """)]
public partial class ConnectPage : IQuickMarkupComponent<Page>
{
    /// <summary>Owning window; set by the consumer before Init so it's ready for use.</summary>
    public WindowController Controller { get; private set; } = null!;

    /// <param name="startup">
    /// Command-line launch target (set by the consumer before the constructor method runs).
    /// When present, the page immediately runs the folder/serve or server connect flow and
    /// navigates to the main chat page on success — the VSCode-style `UnoVibe /path` open.
    /// </param>
    [QuickMarkupConstructor]
    private void Ctor(WindowController controller, StartupArgs? startup)
    {
        Controller = controller;
        RecentConnectionsStore.Load();
        SettingsStore.Load();
        Init(controller, startup);

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
        scrollHost.ViewChanged += (_, _) => UpdateContentMinHeight();
        scrollHost.SizeChanged += OnScrollHostSizeChanged;

        // Pre-fill the server password box from the standard environment variable.
        ServerPassword = Environment.GetEnvironmentVariable(OpencodeClient.PasswordEnvVar) ?? "";

        if (startup is { Kind: not LaunchKind.None })
        {
            _ = RunStartupAsync(startup);
        }
    }

    /// <summary>Viewport width (in pixels) below which the recent/connect panels switch from
    /// the side-by-side two-column layout to the stacked small-screen layout.</summary>
    private const double CompactBreakpoint = 820;

    /// <summary>Keeps the centered content tall enough to fill the viewport so it stays vertically
    /// centered while still scrolling when the window is small.</summary>
    private void UpdateContentMinHeight()
    {
        var h = scrollHost.ViewportHeight;
        if (Math.Abs(content.MinHeight - h) > 0.5) content.MinHeight = h;
    }

    /// <summary>Re-fits the content for the current viewport size: keeps the centered block tall
    /// enough to fill the viewport and switches the recent/connect panels between the side-by-side
    /// desktop layout and the stacked small-screen layout.</summary>
    private void OnScrollHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateContentMinHeight();
        var compact = e.NewSize.Width < CompactBreakpoint;
        if (compact != IsCompact) IsCompact = compact;
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
        try
        {
            var path = await WindowsHelper.PickFolderAsync(Controller.Window, Controller.Store.ServerDirectory);
            if (path is null) return;
            var (ok, password) = ResolveUiFolderPassword();
            if (!ok) return;
            if (await StartServeCoreAsync(path, password))
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

        var serve = new OpencodeServeProcess(password);
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
    private async Task OnOpenRecent(RecentConnection item)
    {
        if (Connecting) return;
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

    private void OnRemoveRecent(string key) => RecentConnectionsStore.Remove(key);

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
            XamlRoot = MarkupNode.XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return null;
        return box.Password;
    }
}
