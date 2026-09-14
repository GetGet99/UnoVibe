using System.Collections.Specialized;

namespace UnoVibe.Pages.Chat;

/// <summary>
/// Chat page message list: the scrollable message panel (revert card, auto-retry card,
/// continue button, and the pending-permission card appended at the end), the empty-state
/// hint, and all stick-to-bottom autoscroll logic.
/// </summary>
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
    ChatboxState Chatbox => `Sessions.ActiveChatbox`;
    string PermissionStage = "choose";
    string RejectText = "";
    // Mirrors the turn.autocontinue setting for the inline switch shown next to the Continue
    // button (two-way bound below; kept in sync with SettingsStore from code-behind).
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
                    // Keyed by message id so QuickMarkup reuses MessageView blocks across
                    // collection resets (session switches/rebuilds) instead of recreating
                    // every element; the revert filter below then only toggles visibility.
                    foreach (var m in `ChatState?.Messages`; `m.Id`)
                    {
                        // Undo: the server keeps reverted messages until the next prompt, so
                        // hide everything at/after the revert point (the card replaces them).
                        if (`(ChatState?.RevertMessageId ?? "").Length == 0 || StringComparer.Ordinal.Compare(m.Id, ChatState?.RevertMessageId ?? "") < 0`)
                            <MessageView Message=`m` RevertRequested+=`OnMessageRevertRequested` ForkRequested+=`OnMessageForkRequested` />
                    }
                    if (`(ChatState?.RevertMessageId ?? "").Length > 0`)
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
                    if (`ChatState?.Retry.IsRetrying == true`)
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
                    if (`ChatState?.ActivePermission is not null`)
                    {
                        <Border Background=`theme.CardBackground` CornerRadius=8 Padding=`new Thickness(12,  10, 12,  10)` Margin=`new Thickness(0, 8, 0, 0)`
                                BorderBrush=`theme.SystemCaution` BorderThickness=`new Thickness(1)` MaxWidth=640 HorizontalAlignment=Left>
                            <StackPanel Spacing=8>
                                <StackPanel Spacing=2>
                                    <TextBlock Text=`ChatState?.ActivePermission?.Title ?? ""` FontSize=13 FontWeight=`FontWeights.SemiBold` TextWrapping=Wrap IsTextSelectionEnabled=true />
                                    if (`(ChatState?.ActivePermission?.Body?.Length ?? 0) > 0`)
                                        <TextBlock Text=`ChatState?.ActivePermission?.Body ?? ""` FontSize=11 Foreground=`theme.SecondaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
                                    if (`(ChatState?.ActivePermission?.PatternsText?.Length ?? 0) > 0`)
                                        <TextBlock Text=`ChatState?.ActivePermission?.PatternsText ?? ""` FontSize=10 Foreground=`theme.TertiaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
                                </StackPanel>
                                if (`PermissionStage == "reject"`)
                                    <StackPanel Spacing=8>
                                        <TextBox Text<=>`RejectText` PlaceholderText="Reason for rejection (optional)" AcceptsReturn=false MinHeight=36 />
                                        <StackPanel Orientation=Horizontal Spacing=8>
                                            <Button Content="Cancel" @Click+=`CancelPermission()` CornerRadius=6 />
                                            <Button Content="Deny" @Click+=`await RejectPermissionAsync()` CornerRadius=6 />
                                        </StackPanel>
                                    </StackPanel>
                                else
                                    <StackPanel Orientation=Horizontal Spacing=8>
                                        <Button Content="Allow once" @Click+=`await AllowPermissionOnceAsync()` CornerRadius=6 />
                                        <Button Content="Always allow" @Click+=`await AllowPermissionAlwaysAsync()` CornerRadius=6 />
                                        <Button Content="Deny…" @Click+=`StartReject()` CornerRadius=6 />
                                    </StackPanel>
                            </StackPanel>
                        </Border>
                    }
                </StackPanel>
            </StickyScrollViewer>
            if (`ChatState?.Messages.Reactive.Count == 0`)
                <StackPanel HorizontalAlignment=Center VerticalAlignment=Center Padding=`new Thickness(16, 0, 16, 0)` Spacing=6 IsHitTestVisible=false>
                    <AppSymbolIcon Symbol=Folder FontSize=22 Foreground=`theme.TertiaryText` HorizontalAlignment=Center />
                    <TextBlock Text=`PathDisplayHelper.Relative(Sessions.ActiveSessionDirectory, Connection.ServerDirectory)` FontSize=13 Foreground=`theme.SecondaryText` TextAlignment=Center TextWrapping=Wrap
                               TextTrimming=`TextTrimming.CharacterEllipsis` MaxWidth=520 ToolTipService.ToolTip=`Sessions.ActiveSessionDirectory` />
                </StackPanel>
        </Grid>
    </root>
    """)]
public partial class ChatMessageList : IQuickMarkupComponent<Grid>
{

    /// <summary>UI-thread dispatcher for bouncing <see cref="SettingsStore.Changed"/> onto the UI thread.</summary>
    private DispatcherQueue? dispatcher;

    /// <summary>
    /// The ChatMessagesState whose Messages collection this component is currently hooked to.
    /// Re-hooked whenever ChatState changes (session switch).
    /// </summary>
    private ChatMessagesState? _hookedChatState;

    [QuickMarkupConstructor]
    private void Ctor()
    {
        UIs.ScrollChatToBottomRequested += scrollHost.ForceScrollToBottom;
        Init();

        // Scrolling keyed off the message panel's laid-out size: SizeChanged fires after the
        // frame's layout pass, so ScrollableHeight reflects the freshly-rendered content
        // (new session messages, streaming parts). Scrolling earlier — right when a message is
        // added to the collection — targets a stale ScrollableHeight of 0 and leaves the
        // viewport at the top.
        messagePanel.SizeChanged += (_, _) => scrollHost.ScrollToBottomIfStick();
        // Messages live on ChatState, which swaps on every session switch.
        // Re-hook the CollectionChanged handler and part hooks whenever ChatState changes.
        ChatStateProp.Watch(HookChatState);
        HookChatState(ChatState);

        // Watch permission changes on ChatState — reset the UI state and scroll to the card.
        ChatStateProp.Watch(newChat =>
        {
            newChat?.ActivePermissionProp.Watch(newReq =>
            {
                if (newReq != ChatState?.ActivePermission) return;
                PermissionStage = "choose";
                RejectText = "";
                _ = ScrollToPermissionAsync();
            });
        });

        // The inline auto-continue switch next to the Continue button mirrors the
        // turn.autocontinue setting two-way: toggling persists immediately (live-apply, like the
        // settings page), and a change from anywhere else (settings overlay / another process)
        // updates the switch. The Changed event may fire off-thread (cross-process file watcher),
        // so bounce to the UI thread.
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
        _ = dispatcher?.TryEnqueue(() =>
        {
            var value = SettingsStore.AutoContinueOnThinking;
            if (AutoContinueOn != value) AutoContinueOn = value;
        });
    }

    /// <summary>Scrolls to the permission card once it has been added to the message list.</summary>
    private async Task ScrollToPermissionAsync()
    {
        await Task.Yield();
        scrollHost.ForceScrollToBottom();
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A full list rebuild (session switch / new session / configure) restarts pinned to
        // the bottom; the freshly-loaded messages then autoscroll into view.
        if (e.Action == NotifyCollectionChangedAction.Reset)
            scrollHost.ForceScrollToBottom();
        if (e.NewItems is not null)
            foreach (MessageItem message in e.NewItems) HookParts(message);
        scrollHost.ScrollToBottomIfStick();
    }

    private void HookParts(MessageItem message) =>
        message.Parts.CollectionChanged += (_, _) => scrollHost.ScrollToBottomIfStick();

    /// <summary>
    /// Resumes a turn that stopped with an error. Sends a "continue" user message — the agent
    /// is instructed (prompt/beast.txt) to pick up from the last incomplete step in its todo
    /// list. Matches the TUI, which has no separate continue API: it's just a user message.
    /// </summary>
    private async Task ContinueAsync()
    {
        await Chatbox.SendManualContinueAsync();
        scrollHost.ForceScrollToBottom();
    }

    /// <summary>Restore reverted messages (redo the undo), then scroll to the end.</summary>
    private async Task RedoLastMessageAsync()
    {
        if (ChatState is not null)
            await ChatState.RedoLastMessageAsync();
        scrollHost.ForceScrollToBottom();
    }

    /// <summary>
    /// Revert to a specific user message (web/TUI parity): rewind the conversation to that
    /// message, restore its prompt into the composer, then scroll to the end.
    /// </summary>
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

    private async Task AllowPermissionOnceAsync()
    {
        var req = ChatState?.ActivePermission;
        if (req is null) return;
        await ChatState!.ReplyPermissionAsync(req.Id, "once");
    }

    private async Task AllowPermissionAlwaysAsync()
    {
        var req = ChatState?.ActivePermission;
        if (req is null) return;
        await ChatState!.ReplyPermissionAsync(req.Id, "always");
    }

    private void StartReject() => PermissionStage = "reject";

    private async Task RejectPermissionAsync()
    {
        var req = ChatState?.ActivePermission;
        if (req is null) return;
        await ChatState!.ReplyPermissionAsync(req.Id, "reject", RejectText.Trim());
    }

    private void CancelPermission() => PermissionStage = "choose";
}
