using UnoVibe.Services;

namespace UnoVibe.Pages.Chat;

/// <summary>
/// Chat page: composes the page-local sub-components (<see cref="ChatHeader"/>,
/// <see cref="ChatStatusArea"/>, <see cref="ChatMessageList"/>, <see cref="ChatComposer"/>)
/// in a vertical layout. Provides the shared composer text (<c>Input</c>) that the message
/// list and composer both read/write, kicks off the connection, and coordinates sends.
/// Each sub-component sits in a single-cell Grid because QuickMarkup forwards attached
/// placement properties (Grid.Row) to the component instance, not its MarkupNode.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Services;
    using QuickMarkup.WinUI;
    inject ChatStore Store;
    provide string Input = "";
    <root>
        <Grid RowDefinitions=<>
            <RowDefinition Height=Auto />
            <RowDefinition Height=Auto />
            <RowDefinition />
            <RowDefinition Height=Auto />
        </>>
            <Grid Grid.Row=0>
                <ChatHeader />
            </Grid>
            <Grid Grid.Row=1>
                <ChatStatusArea />
            </Grid>
            <Grid Grid.Row=2>
                chatMessageList = <ChatMessageList />
            </Grid>
            <Grid Grid.Row=3>
                <ChatComposer SendRequested+=`SendAsync` />
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
        _ = Store.ConnectAsync();
    }

    private async Task SendAsync(string? text, SendPromptMode? mode)
    {
        var content = (text ?? Input).Trim();
        if (content.Length == 0 && Store.Active.PendingImages.Count == 0) return;
        Input = "";
        await Store.Active.SendAsync(content, mode);
        chatMessageList.ForceScrollToBottom();
    }
}
