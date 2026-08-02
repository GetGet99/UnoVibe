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
    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();
        var configured = Environment.GetEnvironmentVariable("OPENCODE_BASE_URL");
        if (!string.IsNullOrWhiteSpace(configured)) Url = configured;
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
        var store = ChatStore.Instance;
        store.Configure(url);
        await store.ConnectAsync();
        Connecting = false;

        if (store.ConnectionStatus == "Connected")
            App.NavigateToMain();
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

        Connecting = true;
        Status = "Starting opencode serve...";

        using var serve = new ServeProcess();
        var result = await serve.StartAsync(Folder);
        if (!result.StartsWith("http://"))
        {
            Status = result;
            Connecting = false;
            return;
        }

        Status = $"Server ready at {result}";
        var store = ChatStore.Instance;
        store.Configure(result);
        await store.ConnectAsync();
        Connecting = false;

        if (store.ConnectionStatus == "Connected")
            App.NavigateToMain();
        else
            Status = store.ConnectionStatus;
    }
}
