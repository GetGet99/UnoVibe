using UnoVibe;
using UnoVibe.Services;

namespace UnoVibe.Pages;

/// <summary>
/// Shown at startup when no OPENCODE_BASE_URL was provided. Lets the user either
/// connect to an existing opencode server or launch a local `opencode serve` from
/// a picked folder, then navigates to the main chat page.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Services;
    using UnoVibe.Pages;
    using QuickMarkup.WinUI;
    string Url = "";
    string Status = "Choose a server to connect to.";
    string Folder = "";
    bool Connecting = false;
    string ServerPassword = "";
    bool UseGeneratedPassword = true;
    string CustomPassword = "";
    string ConfirmPassword = "";
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        <Grid Background=`theme.SolidBackground` RowDefinitions=<>
            <RowDefinition Height=Auto />
            <RowDefinition />
            <RowDefinition Height=Auto />
        </>>
            <TextBlock Grid.Row=0 Text="UnoVibe" FontSize=28 FontWeight=`FontWeights.SemiBold` Padding=`new Thickness(28, 24, 28, 0)` />
            <ScrollViewer Grid.Row=1>
                <StackPanel Padding=`new Thickness(28, 16, 28, 24)` Spacing=16 MaxWidth=640 HorizontalAlignment=Left>
                    <TextBlock Text="Connect to OpenCode" FontSize=18 FontWeight=`FontWeights.SemiBold` />
                    <TextBlock Text=`Status` FontSize=12 Foreground=`theme.SecondaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />

                    <Border Background=`theme.CardBackground` BorderBrush=`theme.CardStroke` BorderThickness=1 CornerRadius=8 Padding=`new Thickness(16, 14, 16, 14)`>
                        <StackPanel Spacing=10>
                            <TextBlock Text="Existing server" FontSize=14 FontWeight=`FontWeights.SemiBold` />
                            <Grid ColumnSpacing=8 ColumnDefinitions=<>
                                <ColumnDefinition />
                                <ColumnDefinition Width=Auto />
                            </>>
                                <TextBox Text<=>`Url` PlaceholderText="http://localhost:4096" IsEnabled=`!Connecting` />
                                <Button Grid.Column=1 Content="Connect" @Click+=`await ConnectToUrlAsync()` IsEnabled=`!Connecting` />
                            </Grid>
                            <PasswordBox Password<=>`ServerPassword` PlaceholderText="Server password (optional)" IsEnabled=`!Connecting` />
                            <TextBlock Text="Leave blank if the server has no password. Uses the OPENCODE_SERVER_PASSWORD environment variable when set." FontSize=11 Foreground=`theme.TertiaryText` TextWrapping=Wrap />
                        </StackPanel>
                    </Border>

                    <Border Background=`theme.CardBackground` BorderBrush=`theme.CardStroke` BorderThickness=1 CornerRadius=8 Padding=`new Thickness(16, 14, 16, 14)`>
                        <StackPanel Spacing=10>
                            <TextBlock Text="Local server" FontSize=14 FontWeight=`FontWeights.SemiBold` />
                            <TextBlock Text="Pick a project folder; the app will run `opencode serve` there and connect to it." FontSize=12 Foreground=`theme.SecondaryText` TextWrapping=Wrap />
                            <TextBlock Text=`Folder.Length > 0 ? Folder : "(no folder selected)"` FontSize=12 Foreground=`Folder.Length > 0 ? theme.PrimaryText : theme.TertiaryText` TextTrimming=`TextTrimming.CharacterEllipsis` />
                            <StackPanel Orientation=Horizontal Spacing=8>
                                <Button Content="Choose folder..." @Click+=`await PickFolderAsync()` IsEnabled=`!Connecting` />
                                <Button Content="Start & connect" @Click+=`await StartServeAsync()` IsEnabled=`!Connecting && Folder.Length > 0` />
                            </StackPanel>
                            <ToggleSwitch Header="Server security" OnContent="Use a generated strong password" OffContent="Set my own password" IsOn<=>`UseGeneratedPassword` IsEnabled=`!Connecting` />
                            if (`!UseGeneratedPassword`) {
                                <PasswordBox Password<=>`CustomPassword` PlaceholderText="Set a password" IsEnabled=`!Connecting` />
                                <PasswordBox Password<=>`ConfirmPassword` PlaceholderText="Confirm password" IsEnabled=`!Connecting` />
                            }
                        </StackPanel>
                    </Border>
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
        Init();
        var configured = Environment.GetEnvironmentVariable("OPENCODE_BASE_URL");
        if (!string.IsNullOrWhiteSpace(configured)) Url = configured;
        ServerPassword = Environment.GetEnvironmentVariable(OpencodeClient.PasswordEnvVar) ?? "";
    }

    private async Task ConnectToUrlAsync()
    {
        var url = Url.Trim();
        if (url.Length == 0) url = "http://localhost:4096";
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "http://" + url;

        Connecting = true;
        Status = $"Connecting to {url}...";
        var store = Controller.Store;
        var password = ServerPassword.Trim();
        store.Configure(url, password.Length > 0 ? password : null);
        await store.ConnectAsync();
        Connecting = false;

        if (store.ConnectionStatus == "Connected")
            Controller.ShowMain();
        else
            Status = store.ConnectionStatus;
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

    private async Task StartServeAsync()
    {
        if (Folder.Length == 0) return;

        string? password = null;
        if (!UseGeneratedPassword)
        {
            if (CustomPassword.Length == 0)
            {
                Status = "Please set a password.";
                return;
            }
            if (CustomPassword != ConfirmPassword)
            {
                Status = "Passwords do not match.";
                return;
            }
            password = CustomPassword;
        }

        Connecting = true;
        Status = "Starting opencode serve...";

        var serve = new ServeProcess(password);
        var result = await serve.StartAsync(Folder);
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
            Controller.ShowMain();
        else
            Status = store.ConnectionStatus;
    }
}
