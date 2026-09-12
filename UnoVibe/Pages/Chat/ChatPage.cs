using UnoVibe.Models;
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
    using UnoVibe.Providers;
    using UnoVibe.Integration;
    using QuickMarkup.WinUI;
    inject SessionsSource Sessions;
    inject ToastService Toasts;
    inject OpencodeClient Opencode;
    inject UIService UIs;
    provide ChatPage ChatP = `this`;
    <root>
        <Grid RowDefinitions=<>
            <RowDefinition Height=Auto />
            <RowDefinition Height=Auto />
            <RowDefinition />
            <RowDefinition Height=Auto />
        </>>
            <Grid Grid.Row=0>
                header = <ChatHeader />
            </Grid>
            <Grid Grid.Row=1>
                <ChatStatusArea />
            </Grid>
            <Grid Grid.Row=2>
                chatMessageList = <ChatMessageList />
            </Grid>
            <Grid Grid.Row=3>
                composer = <ChatComposer />
            </Grid>
        </Grid>
    </root>
    """)]
public partial class ChatPage : Page
{
    [QuickMarkupConstructor]
    void Ctor()
    {
        UIs.ForkAndSwitchSessionRequested += ForkAndSwitchSession;
        UIs.ForkAndSwitchSessionWithMessageRequested += ForkAndSwitchSession;
        Init();
        
    }

    /// <summary>Enters the header's imposer, then scroll to the end.</summary>
    public async Task UndoLastAsync()
    {
        await StoreToUpdate.Active.UndoLastMessageAsync();
        if (StoreToUpdate.Active.RevertPromptText.Length > 0)
            composer.SetChatText(StoreToUpdate.Active.RevertPromptText);
        UIs.ScrollChatToBottom();
    }

    /// <summary>Restore reverted messages (/redo built-in), then scroll to the end.</summary>
    public async Task RedoLastAsync()
    {
        await StoreToUpdate.Active.RedoLastMessageAsync();
        UIs.ScrollChatToBottom();
    }


    /// <summary>
    /// Forks the conversation at a specific message (TUI/web parity: "Fork" action). Calls
    /// POST /session/{id}/fork with the target message id — the server creates a new session
    /// containing all messages strictly before the fork point (the forked-at message itself is
    /// excluded) titled "&lt;original&gt; (fork #N)" — then switches to it and restores the
    /// forked-at message's prompt (text + staged images) into the composer so the user can
    /// continue from there. Returns the new session id, or null on failure/no session.
    /// </summary>
    async void ForkAndSwitchSession(SessionId sessionId, MessageItem message)
    {
        var forkedResult = await Opencode.ForkSessionAsync(sessionId, new()
        {
            MessageID = message.Id
        });
        if (!forkedResult.TryGetValue(out var forked, out var error))
        {
            Toasts.ShowError(error, "Fork failed");
            return;
        }

        var head = Sessions.Register(forked);

        Sessions.EnsureChatbox(sessionId).ReplaceFromMessage(message);
        Sessions.ActiveSessionId = head.Id;
        
        return;
    }


    /// <summary>
    /// Forks the whole active session (TUI/web parity: "Full session" fork). Calls
    /// POST /session/{id}/fork with no message id so the server copies every message and titles
    /// the new session "&lt;original&gt; (fork #N)", then switches to it. Unlike the per-message
    /// fork there's no prompt to restore — the composer keeps whatever the user had. Returns the
    /// new session id, or null on failure/no session.
    /// </summary>
    async void ForkAndSwitchSession(SessionId sessionId)
    {
        var forkedResult = await Opencode.ForkSessionAsync(sessionId, new());
        if (!forkedResult.TryGetValue(out var forked, out var error))
        {
            Toasts.ShowError(error, "Fork failed");
            return;
        }

        var head = Sessions.Register(forked);

        Sessions.ActiveSessionId = head.Id;
    }
}
