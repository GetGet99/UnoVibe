namespace UnoVibe.Pages.Chat;

[QuickMarkup("""
    using QuickMarkup.WinUI;
    using UnoVibe.States;
    inject SessionsStateProvider Sessions;
    inject ToastsProvider Toasts;
    inject OpencodeClient Opencode;
    inject UIServiceProvider UIs;
    inject EventsProvider Events;
    inject ModelsProvider Models;
    provide ChatPage ChatP = `this`;
    provide ChatMessagesState? ChatState;
    ChatMessagesState? ChatStateGetter => async `GetSessionAsync()`;
    provide `AsyncComputed<ChatMessagesState>` ChatStateAsync = `ChatStateGetterAsync`;
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
partial class ChatPage : Page
{
    [QuickMarkupConstructor]
    void Ctor()
    {
        UIs.ForkAndSwitchSessionRequested += ForkAndSwitchSession;
        UIs.ForkAndSwitchSessionWithMessageRequested += ForkAndSwitchSession;
        WatchSession();
        Init();
    }
    async Task<ChatMessagesState?> GetSessionAsync()
    {
        if (Sessions.ActiveSessionId is {} sessionId)
            return await ChatMessagesState.Create(Opencode, Toasts, Events, Models, Sessions, sessionId);
        return null;
    }

    void WatchSession()
    {
        ChatStateGetterAsync.Watch(x =>
        {
            if (ChatState is {} value)
            {
                value.Dispose();
                value = null;
                CallGCAfterDelay(2000);

            }
            if (x.IsFailed)
                Toasts.ShowError(
                    x.Failure!.ToString(),
                    "Chat State"
                );

            ChatState = x.IsSuccess ? x.Value : null;
        }, immediate: true);
    }

    static async void CallGCAfterDelay(int ms)
    {
        await Task.Delay(ms).ConfigureAwait(continueOnCapturedContext: false);
        GC.Collect();
    }

    public async Task UndoLastAsync()
    {
        if (ChatState is not null)
        {
            await ChatState.UndoLastMessageAsync();
        }
        UIs.ScrollChatToBottom();
    }

    public async Task RedoLastAsync()
    {
        if (ChatState is not null)
        {
            await ChatState.RedoLastMessageAsync();
        }
        UIs.ScrollChatToBottom();
    }

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

        Sessions.ActiveSessionId = head.Id;
        Sessions.ActiveChatbox.Message = ChatboxMessage.From(message);

        return;
    }

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
