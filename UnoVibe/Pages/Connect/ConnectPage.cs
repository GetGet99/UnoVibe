using UnoVibe.Services;
using UnoVibe.Integration;
using UnoVibe.Models.Startup;

namespace UnoVibe.Pages.Connect;

[QuickMarkup("""
    using UnoVibe.Services;
    using QuickMarkup.WinUI;
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
partial class ConnectPage : IQuickMarkupComponent<Page>
{
    public WindowController Controller { get; private set; } = null!;

    [QuickMarkupConstructor]
    private void Ctor(WindowController controller, StartupArgs? startup)
    {
        Controller = controller;
        RecentConnectionsStore.Load();
        SettingsStore.Load();
        Init(controller, startup);

        UseGeneratedPassword = RecentConnectionsStore.UseGeneratedPassword;
        SaveFolderPassword = RecentConnectionsStore.SaveFolderPassword;
        CustomPassword = RecentConnectionsStore.CustomPassword;
        if (SaveFolderPassword && CustomPassword.Length > 0)
            ConfirmPassword = CustomPassword;

        scrollHost.ViewChanged += (_, _) => UpdateContentMinHeight();
        scrollHost.SizeChanged += OnScrollHostSizeChanged;

        ServerPassword = Environment.GetEnvironmentVariable(OpencodeHelper.PasswordEnvVar) ?? "";

        if (startup is { Kind: not LaunchKind.None })
        {
            _ = RunStartupAsync(startup);
        }
    }

    private const double CompactBreakpoint = 820;

    private void UpdateContentMinHeight()
    {
        var h = scrollHost.ViewportHeight;
        if (Math.Abs(content.MinHeight - h) > 0.5) content.MinHeight = h;
    }

    private void OnScrollHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateContentMinHeight();
        var compact = e.NewSize.Width < CompactBreakpoint;
        if (compact != IsCompact) IsCompact = compact;
    }

    private async Task ConnectToUrlAsync() =>
        await ConnectCoreAsync(Url, ServerPassword.Trim() is { Length: > 0 } p ? p : null);

    private async Task ConnectCoreAsync(string url, string? password)
    {
        var clean = url.Trim();
        if (clean.Length == 0) clean = "http://localhost:4096";
        if (!clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            clean = "http://" + clean;

        Connecting = true;
        Status = $"Connecting to {clean}...";
        var connection = await OpencodeConnection.FromAsync(clean, username: null, password);
        Connecting = false;

        if (connection.ConnectionStatus == "Connected")
        {
            RecentConnectionsStore.UpsertServer(clean, password is { Length: > 0 });
            Controller.ShowMain(connection);
        }
        else
        {
            Status = connection.ConnectionStatus;
        }
    }

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

    private async Task PickFolderAsync()
    {
        try
        {
            var path = await WindowsHelper.PickFolderAsync(Controller.Window, startPath: null);
            if (path is null) return;
            var (ok, password) = ResolveUiFolderPassword();
            if (!ok) return;
            if (await StartServeCoreAsync(path, password) is {} conn)
            {
                RecentConnectionsStore.SaveSecurity(UseGeneratedPassword, SaveFolderPassword, CustomPassword);
                Controller.ShowMain(conn);
            }
        }
        catch (Exception ex)
        {
            Status = $"Folder picker error: {ex.Message}";
        }
    }

    private async Task<OpencodeConnection?> StartServeCoreAsync(string folder, string? password)
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
            return null;
        }

        Status = $"Server ready at {result}";
        var connection = await OpencodeConnection.FromAsync(serve);
        Connecting = false;

        if (connection.ConnectionStatus != "Connected")
        {
            Status = connection.ConnectionStatus;
            return null;
        }

        RecentConnectionsStore.UpsertFolder(folder);
        return connection;
    }

    private async Task RunStartupAsync(StartupArgs startup)
    {
        switch (startup.Kind)
        {
            case LaunchKind.Folder:
                if (await StartServeCoreAsync(startup.StartParam, startup.Password) is {} conn)
                    Controller.ShowMain(conn);
                break;
            case LaunchKind.Server:
                await ConnectCoreAsync(startup.StartParam, startup.Password);
                break;
        }
    }

    private async Task OnOpenRecent(RecentConnection item)
    {
        if (Connecting) return;
        if (item.IsFolder)
        {
            var (ok, password) = ResolveUiFolderPassword();
            if (!ok) return;
            if (await StartServeCoreAsync(item.Detail, password) is {} conn)
            {
                RecentConnectionsStore.SaveSecurity(UseGeneratedPassword, SaveFolderPassword, CustomPassword);
                Controller.ShowMain(conn);
            }
        }
        else
        {
            string? password = null;
            if (item.RequiresPassword)
            {
                password = await PromptForServerPasswordAsync(item.Detail);
                if (password is null) return;
                if (password.Length == 0) password = null;
            }
            await ConnectCoreAsync(item.Detail, password);
        }
    }

    private void OnRemoveRecent(string key) => RecentConnectionsStore.Remove(key);

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
