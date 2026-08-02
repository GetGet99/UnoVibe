using System.Collections.Specialized;
using UnoVibe.Models;
using UnoVibe.Services;

namespace UnoVibe.Pages;

[QuickMarkup("""
    using UnoVibe.Models;
    using UnoVibe.Services;
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    string Input = "";
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        <Grid RowDefinitions=<>
            <RowDefinition Height=Auto />
            <RowDefinition />
            <RowDefinition Height=Auto />
        </>>
            <Grid Grid.Row=0 ColumnSpacing=8 Padding=`new Thickness(16, 12, 16, 8)` ColumnDefinitions=<>
                <ColumnDefinition />
                <ColumnDefinition Width=Auto />
            </>>
                <StackPanel>
                    <TextBlock Text=`ChatStore.Instance.SessionTitle` FontSize=16 FontWeight=`FontWeights.SemiBold` />
                    <TextBlock Text=`ChatStore.Instance.ConnectionStatus` FontSize=11 Foreground=`theme.SecondaryText` />
                </StackPanel>
                <ProgressRing Grid.Column=1 Width=20 Height=20 IsActive=`ChatStore.Instance.IsBusy`
                              Visibility=`ChatStore.Instance.IsBusy ? Visibility.Visible : Visibility.Collapsed` />
            </Grid>
            <Grid Grid.Row=1>
                scrollHost = <ScrollViewer>
                    <StackPanel Padding=16>
                        foreach (var m in `ChatStore.Instance.Messages`)
                            <MessageView Message=`m` />
                    </StackPanel>
                </ScrollViewer>
            </Grid>
            <Grid Grid.Row=2 ColumnSpacing=8 Padding=`new Thickness(16, 8, 16, 16)` ColumnDefinitions=<>
                <ColumnDefinition />
                <ColumnDefinition Width=Auto />
            </>>
                inputBox = <TextBox Text<=>`Input` PlaceholderText="Message OpenCode..." AcceptsReturn=false KeyDown+=`OnInputKeyDown` />
                <Button Grid.Column=1 Content="Send" @Click+=`await SendAsync()` IsEnabled=`!ChatStore.Instance.IsBusy` />
            </Grid>
        </Grid>
    </root>
    """)]
public partial class ChatPage : Page
{
    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();

        var store = ChatStore.Instance;
        store.Messages.CollectionChanged += OnMessagesChanged;
        foreach (var message in store.Messages) HookParts(message);

        _ = store.ConnectAsync();
        inputBox.Focus(FocusState.Programmatic);
    }

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        _ = SendAsync();
    }

    private async Task SendAsync()
    {
        var text = Input.Trim();
        if (text.Length == 0) return;
        Input = "";
        await ChatStore.Instance.SendAsync(text);
        ScrollToBottom();
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (MessageItem message in e.NewItems) HookParts(message);
        ScrollToBottom();
    }

    private void HookParts(MessageItem message) =>
        message.Parts.CollectionChanged += (_, _) => ScrollToBottom();

    private void ScrollToBottom()
    {
        if (scrollHost is null) return;
        scrollHost.ChangeView(null, scrollHost.ScrollableHeight, null);
    }
}
