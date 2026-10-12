namespace UnoVibe.Pages.Home;

[QuickMarkup("""
    using UnoVibe.Controls;
    using UnoVibe.Pages.Chat;
    using QuickMarkup.WinUI;
    using Microsoft.UI;
    inject HomeProvider Home;
    inject SessionsStateProvider Sessions;
    inject OpencodeConnection Connection;
    inject ToastsProvider Toasts;
    inject Window HostWindow;
    inject bool IsCompact;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <Border Background=`theme.CardBackground` BorderBrush=`theme.CardStroke` BorderThickness=`new Thickness(1, 1, 1, 1)` CornerRadius=8 Padding=`new Thickness(12, 10, 12, 10)`>
        <StackPanel Spacing=8>
            <Grid ColumnSpacing=8 ColumnDefinitions=<>
                <ColumnDefinition Width=Auto />
                <ColumnDefinition />
                <ColumnDefinition Width=Auto />
            </>>
                <Border Grid.Column=0 Width=36 Height=36 CornerRadius=18 Background=`theme.SolidBackground` BorderBrush=`theme.DividerStroke` BorderThickness=`new Thickness(1, 1, 1, 1)` VerticalAlignment=Center>
                    <AppSymbolIcon Symbol=Message FontSize=16 Foreground=`theme.PrimaryText` HorizontalAlignment=Center VerticalAlignment=Center />
                </Border>
                <StackPanel Grid.Column=1 Spacing=2 VerticalAlignment=Center>
                    <TextBlock Text="New chat" FontSize=13 FontWeight=`FontWeights.SemiBold` VerticalAlignment=Center />
                    <TextBlock Text="Pick a folder, then send your first message." FontSize=11 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                </StackPanel>
                <StackPanel Grid.Column=2 Orientation=Horizontal Spacing=4 VerticalAlignment=Top>
                    <SmallButtonIcon Symbol=Cancel Tooltip="Discard and close" @Click+=`Home.CancelCreating()` />
                </StackPanel>
            </Grid>
            <StackPanel Orientation=Horizontal Spacing=8 VerticalAlignment=Center>
                <ComboBox
                    ItemsSource=`Home.DraftDirectoryOptions.ToArray()`
                    SelectedItem=`Home.DraftDirectory`
                    ItemTemplate=template (string? value) { <TextBlock Text=`DisplayPath(value)` /> }
                    SelectedItem+=>`x => Home.SetDraftDirectory(x as string ?? "")`
                    MinWidth=`IsCompact ? 160 : 220`
                    Height=28
                    FontSize=12
                />
                <TextBlock Text="or" FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                <Button Content="Browse…" Padding=`new Thickness(10, 3, 10, 3)` CornerRadius=6 @Click+=`await PickCustomDirectoryAsync()` ToolTipService.ToolTip="Pick a custom folder" />
            </StackPanel>
            if (`Home.DraftDirectory is not null`)
            {
                <ChatComposer Chatbox=`Sessions.ActiveChatbox` ChatParams=`Sessions.ActiveChatParams`
                    SessionDirectory=`Home.DraftDirectory ?? Sessions.ActiveSessionDirectory` Head=null
                    Sent+=`() => OnDraftSent()`
                    EnsureSessionRequested+=`() => EnsureDraftChatboxAsync()` />
            }
            else
            {
                <TextBlock Text="Pick a folder to start." FontSize=12 Foreground=`theme.SecondaryText` />
            }
        </StackPanel>
    </Border>
    """)]
partial class HomeNewSessionCard : IQuickMarkupComponent
{
    private string DisplayPath(string? fullPath) =>
        fullPath is null ? "" : PathDisplayHelper.Relative(fullPath, Connection.ServerDirectory);

    private void OnDraftSent()
    {
        if (Sessions.ActiveSessionId is {} id)
            Home.CompleteDraft(id);
    }

    private Task<ChatboxState> EnsureDraftChatboxAsync() => Home.EnsureDraftChatboxAsync();

    private async Task PickCustomDirectoryAsync()
    {
        try
        {
            var path = await WindowsHelper.PickFolderAsync(HostWindow, Connection.ServerDirectory);
            if (path is null) return;
            await Sessions.AddDirectoryAsync(path);
            Home.SetDraftDirectory(path);
        }
        catch (Exception ex)
        {
            Toasts.ShowError(ex.Message, "Folder picker failed");
        }
    }
}
