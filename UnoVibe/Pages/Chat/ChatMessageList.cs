using System.Collections.Specialized;

namespace UnoVibe.Pages.Chat;

[QuickMarkup("""
    using UnoVibe.Controls;
    using UnoVibe.Helpers;
    using UnoVibe.States;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    inject UIServiceProvider UIs;
    inject SessionsStateProvider Sessions;
    inject OpencodeConnection Connection;
    inject ChatMessagesState? ChatState;
    inject `AsyncComputed<ChatMessagesState>` ChatStateAsync;
    ChatboxState Chatbox => `Sessions.ActiveChatbox`;
    public bool AutoContinueOn = `SettingsStore.AutoContinueOnThinking`;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        <Grid>
            scrollHost = <StickyScrollViewer>
                messagePanel = <StackPanel Padding=16>
                    if (`ChatState?.TruncatedMessagesCount > 0`)
                        <Border Background=`theme.CardBackground` CornerRadius=6 Padding=`new Thickness(10,  8, 10,  8)` Margin=`new Thickness(0, 0, 0, 8)`>
                            <TextBlock Text=`$"History truncated: {ChatState?.TruncatedMessagesCount} earlier message(s) removed for performance."` FontSize=11 Foreground=`theme.SecondaryText` TextWrapping=Wrap />
                        </Border>
                    foreach (var m in `ChatState?.Messages`; `m.Id`)
                    {
                        if (`(ChatState?.RevertMessageId ?? "").Length == 0 || StringComparer.Ordinal.Compare(m.Id, ChatState?.RevertMessageId ?? "") < 0`)
                            <MessageView Message=`m` RevertRequested+=`OnMessageRevertRequested` ForkRequested+=`OnMessageForkRequested` QuestionAnswered+=`OnQuestionAnswered` QuestionRejected+=`OnQuestionRejected` />
                    }
                    if (`ChatState?.RevertMessageId`)
                    {
                        <Border Background=`theme.CardBackground` CornerRadius=8 Padding=`new Thickness(12,  10, 12,  10)` Margin=`new Thickness(0, 8, 0, 0)`
                                BorderBrush=`theme.SystemCaution` BorderThickness=`new Thickness(1)` MaxWidth=640 HorizontalAlignment=Left>
                            <StackPanel Spacing=6>
                                <StackPanel Orientation=Horizontal Spacing=8>
                                    <AppSymbolIcon Symbol=Undo FontSize=14 Foreground=`theme.SystemCaution` VerticalAlignment=Center />
                                    <TextBlock Text=`ChatState?.RevertCount == 1 ? "1 message reverted" : $"{ChatState?.RevertCount} messages reverted"` FontSize=12 FontWeight=`FontWeights.SemiBold` VerticalAlignment=Center />
                                </StackPanel>
                                <StackPanel Orientation=Horizontal Spacing=8>
                                    <Button Content="Redo" @Click+=`await RedoLastMessageAsync()` CornerRadius=6 Padding=`new Thickness(10,  4, 10,  4)` />
                                    <TextBlock Text="Click redo to restore the reverted messages and continue from here." FontSize=11 Foreground=`theme.SecondaryText` TextWrapping=Wrap VerticalAlignment=Center />
                                </StackPanel>
                            </StackPanel>
                        </Border>
                    }
                    if (`ChatState?.Retry.IsRetrying`)
                        <Border Background=`theme.SystemCautionBackground` CornerRadius=8 Padding=`new Thickness(12,  10, 12,  10)` Margin=`new Thickness(0, 8, 0, 0)`
                                BorderBrush=`theme.SystemCaution` BorderThickness=`new Thickness(1)` MaxWidth=640 HorizontalAlignment=Left>
                            <ChatRetryCard />
                        </Border>
                    if (`Chatbox.ShowContinue`)
                        <StackPanel Orientation=Horizontal Spacing=8 Margin=`new Thickness(0, 8, 0, 0)` HorizontalAlignment=Left>
                            <Button Content="⟳ Continue" CornerRadius=6 VerticalAlignment=Center
                                    ToolTipService.ToolTip=`"Sends a message with content \"continue\" to resume the work from the last incomplete step."`
                                    @Click+=`await ContinueAsync()` />
                            <ToggleSwitch OnContent="auto continue" OffContent="auto continue" IsOn<=>`AutoContinueOn`
                                          FontSize=12 VerticalAlignment=Center
                                          ToolTipService.ToolTip=`"When on, a turn that stops with the chat ending on an unfinished Thinking block is continued automatically — no completion notification and no sidebar check mark. Same as the \"Auto-continue on thinking stop\" setting."` />
                        </StackPanel>
                    if (`ChatState?.ActivePermission`)
                    {
                        <PermissionBanner Request=`ChatState!.ActivePermission!` AllowOnceRequested+=`OnAllowOnce` AllowAlwaysRequested+=`OnAllowAlways` RejectRequested+=`OnRejectPermission` />
                    }
                </StackPanel>
            </StickyScrollViewer>
            if (`(ChatState?.Messages.Reactive.Count ?? 0) == 0`)
                if (`ChatStateAsync.IsLoading`)
                    <StackPanel HorizontalAlignment=Center VerticalAlignment=Center Padding=`new Thickness(16, 0, 16, 0)` Spacing=6 IsHitTestVisible=false>
                        <ProgressRing Width=32 Height=32 IsActive=true />
                        <TextBlock Text=`PathDisplayHelper.Relative(Sessions.ActiveSessionDirectory, Connection.ServerDirectory)` FontSize=13 Foreground=`theme.SecondaryText` TextAlignment=Center TextWrapping=Wrap
                                TextTrimming=`TextTrimming.CharacterEllipsis` MaxWidth=520 ToolTipService.ToolTip=`Sessions.ActiveSessionDirectory` />
                    </StackPanel>
                else
                    <StackPanel HorizontalAlignment=Center VerticalAlignment=Center Padding=`new Thickness(16, 0, 16, 0)` Spacing=6 IsHitTestVisible=false>
                        <AppSymbolIcon Symbol=Folder FontSize=22 Foreground=`theme.TertiaryText` HorizontalAlignment=Center />
                        <TextBlock Text=`PathDisplayHelper.Relative(Sessions.ActiveSessionDirectory, Connection.ServerDirectory)` FontSize=13 Foreground=`theme.SecondaryText` TextAlignment=Center TextWrapping=Wrap
                                TextTrimming=`TextTrimming.CharacterEllipsis` MaxWidth=520 ToolTipService.ToolTip=`Sessions.ActiveSessionDirectory` />
                    </StackPanel>
        </Grid>
    </root>
    """)]
partial class ChatMessageList : IQuickMarkupComponent<Grid>
{

    private DispatcherQueue? dispatcher;

    private ChatMessagesState? _hookedChatState;

    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();
        UIs.ScrollChatToBottomRequested += scrollHost.ForceScrollToBottom;

        messagePanel.SizeChanged += (_, _) => scrollHost.ScrollToBottomIfStick();
        ChatStateProp.Watch(HookChatState);
        HookChatState(ChatState);

        ChatStateProp.Watch(newChat =>
        {
            newChat?.ActivePermissionProp.Watch(newReq =>
            {
                if (newReq != ChatState?.ActivePermission) return;
                _ = ScrollToPermissionAsync();
            });
        });

        dispatcher = DispatcherQueue.GetForCurrentThread();
        AutoContinueOnProp.Watch(on => SettingsStore.SetValue(SettingsStore.AutoContinueKey, on ? "true" : "false"));
        SettingsStore.Changed += OnSettingsChanged;
    }

    private void HookChatState(ChatMessagesState? newState)
    {
        _hookedChatState?.Messages.CollectionChanged -= OnMessagesChanged;
        _hookedChatState = newState;
        if (newState is not null)
        {
            newState.Messages.CollectionChanged += OnMessagesChanged;
            foreach (var message in newState.Messages) HookParts(message);
        }
        scrollHost.ForceScrollToBottom();
    }

    private void OnSettingsChanged()
    {
        _ = dispatcher?.RunOrEnqueue(() =>
        {
            var value = SettingsStore.AutoContinueOnThinking;
            if (AutoContinueOn != value) AutoContinueOn = value;
        });
    }

    private async Task ScrollToPermissionAsync()
    {
        await Task.Yield();
        scrollHost.ForceScrollToBottom();
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
            scrollHost.ForceScrollToBottom();
        if (e.NewItems is not null)
            foreach (MessageItem message in e.NewItems) HookParts(message);
        scrollHost.ScrollToBottomIfStick();
    }

    private void HookParts(MessageItem message) =>
        message.Parts.CollectionChanged += (_, _) => scrollHost.ScrollToBottomIfStick();

    private async Task ContinueAsync()
    {
        await Chatbox.SendManualContinueAsync();
        scrollHost.ForceScrollToBottom();
    }

    private async Task RedoLastMessageAsync()
    {
        if (ChatState is not null)
            await ChatState.RedoLastMessageAsync();
        scrollHost.ForceScrollToBottom();
    }

    private async Task OnMessageRevertRequested(MessageItem message)
    {
        if (ChatState is not null)
            await ChatState.RevertToMessageAsync(message);
        Sessions.ActiveChatbox.Message = ChatboxMessage.From(message);
        scrollHost.ForceScrollToBottom();
    }

    private Task OnMessageForkRequested(MessageItem m)
    {
        if (Sessions.ActiveSessionId is {} id)
            UIs.ForkAndSwitchSession(id, m);
        return Task.CompletedTask;
    }

    private async Task OnAllowOnce(string requestId)
    {
        if (ChatState is null) return;
        await ChatState.ReplyPermissionAsync(requestId, "once");
    }

    private async Task OnAllowAlways(string requestId)
    {
        if (ChatState is null) return;
        await ChatState.ReplyPermissionAsync(requestId, "always");
    }

    private async Task OnRejectPermission(string requestId, string? message)
    {
        if (ChatState is null) return;
        await ChatState.ReplyPermissionAsync(requestId, "reject", message);
    }

    private async Task OnQuestionAnswered(string requestId, IReadOnlyList<IReadOnlyList<string>> answers)
    {
        if (ChatState is null) return;
        await ChatState.ReplyQuestionAsync(requestId, answers);
    }

    private async Task OnQuestionRejected(string requestId)
    {
        if (ChatState is null) return;
        await ChatState.RejectQuestionAsync(requestId);
    }
}
