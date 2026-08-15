using System.Collections.Specialized;
using UnoVibe.Models;
using UnoVibe.Services;

namespace UnoVibe.Pages.Chat;

/// <summary>
/// Chat page message list: the scrollable message panel (revert card, auto-retry card,
/// continue button, and the pending-permission card appended at the end), the empty-state
/// hint, and all stick-to-bottom autoscroll logic.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Services;
    using UnoVibe.Models;
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    inject ChatStore Store;
    inject string Input;
    string PermissionStage = "choose";
    string RejectText = "";
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        <Grid>
            scrollHost = <ScrollViewer>
                messagePanel = <StackPanel Padding=16>
                    if (`Store.Active.HiddenMessages > 0`)
                        <Border Background=`theme.CardBackground` CornerRadius=6 Padding=`new Thickness(10,  8, 10,  8)` Margin=`new Thickness(0, 0, 0, 8)`>
                            <TextBlock Text=`$"History truncated: {Store.Active.HiddenMessages} earlier message(s) removed for performance."` FontSize=11 Foreground=`theme.SecondaryText` TextWrapping=Wrap />
                        </Border>
                    // Keyed by message id so QuickMarkup reuses MessageView blocks across
                    // collection resets (session switches/rebuilds) instead of recreating
                    // every element; the revert filter below then only toggles visibility.
                    foreach (var m in `Store.Active.Messages`; `m.Id`)
                    {
                        // Undo: the server keeps reverted messages until the next prompt, so
                        // hide everything at/after the revert point (the card replaces them).
                        if (`Store.Active.RevertMessageId.Length == 0 || StringComparer.Ordinal.Compare(m.Id, Store.Active.RevertMessageId) < 0`)
                            <MessageView Message=`m` RevertRequested+=`OnMessageRevertRequested` ForkRequested+=`OnMessageForkRequested` />
                    }
                    if (`Store.Active.RevertMessageId.Length > 0`)
                    {
                        <Border Background=`theme.CardBackground` CornerRadius=8 Padding=`new Thickness(12,  10, 12,  10)` Margin=`new Thickness(0, 8, 0, 0)`
                                BorderBrush=`theme.SystemCaution` BorderThickness=`new Thickness(1)` MaxWidth=640 HorizontalAlignment=Left>
                            <StackPanel Spacing=6>
                                <StackPanel Orientation=Horizontal Spacing=8>
                                    <AppSymbolIcon Symbol=Undo FontSize=14 Foreground=`theme.SystemCaution` VerticalAlignment=Center />
                                    <TextBlock Text=`Store.Active.RevertCountLabel` FontSize=12 FontWeight=`FontWeights.SemiBold` VerticalAlignment=Center />
                                </StackPanel>
                                <StackPanel Orientation=Horizontal Spacing=8>
                                    <Button Content="Redo" @Click+=`await RedoLastMessageAsync()` CornerRadius=6 Padding=`new Thickness(10,  4, 10,  4)` />
                                    <TextBlock Text="Click redo to restore the reverted messages and continue from here." FontSize=11 Foreground=`theme.SecondaryText` TextWrapping=Wrap VerticalAlignment=Center />
                                </StackPanel>
                            </StackPanel>
                        </Border>
                    }
                    if (`Store.Active.IsRetrying`)
                        <Border Background=`theme.SystemCautionBackground` CornerRadius=8 Padding=`new Thickness(12,  10, 12,  10)` Margin=`new Thickness(0, 8, 0, 0)`
                                BorderBrush=`theme.SystemCaution` BorderThickness=`new Thickness(1)` MaxWidth=640 HorizontalAlignment=Left>
                            <StackPanel Spacing=6>
                                <StackPanel Orientation=Horizontal Spacing=8>
                                    <ProgressRing Width=14 Height=14 IsActive=true VerticalAlignment=Center />
                                    <TextBlock Text="Auto-retrying" FontSize=12 FontWeight=`FontWeights.SemiBold` VerticalAlignment=Center />
                                </StackPanel>
                                if (`Store.Active.RetryMessage.Length > 0`)
                                    <TextBlock Text=`Store.Active.RetryMessage` FontSize=12 Foreground=`theme.SecondaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
                                <TextBlock Text=`Store.Active.RetryCountdown` FontSize=11 Foreground=`theme.SystemCaution` TextWrapping=Wrap />
                            </StackPanel>
                        </Border>
                    if (`Store.Active.ShowContinue`)
                        <Button Content="⟳ Continue" CornerRadius=6 HorizontalAlignment=Left Margin=`new Thickness(0, 8, 0, 0)`
                                ToolTipService.ToolTip=`"Sends a message with content \"continue\" to resume the work from the last incomplete step."`
                                @Click+=`await ContinueAsync()` />
                    if (`Store.ActivePermission is not null`)
                    {
                        <Border Background=`theme.CardBackground` CornerRadius=8 Padding=`new Thickness(12,  10, 12,  10)` Margin=`new Thickness(0, 8, 0, 0)`
                                BorderBrush=`theme.SystemCaution` BorderThickness=`new Thickness(1)` MaxWidth=640 HorizontalAlignment=Left>
                            <StackPanel Spacing=8>
                                <StackPanel Spacing=2>
                                    <TextBlock Text=`Store.ActivePermission?.Title ?? ""` FontSize=13 FontWeight=`FontWeights.SemiBold` TextWrapping=Wrap IsTextSelectionEnabled=true />
                                    if (`(Store.ActivePermission?.Body?.Length ?? 0) > 0`)
                                        <TextBlock Text=`Store.ActivePermission?.Body ?? ""` FontSize=11 FontFamily="Consolas" Foreground=`theme.SecondaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
                                    if (`(Store.ActivePermission?.PatternsText?.Length ?? 0) > 0`)
                                        <TextBlock Text=`Store.ActivePermission?.PatternsText ?? ""` FontSize=10 FontFamily="Consolas" Foreground=`theme.TertiaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
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
            </ScrollViewer>
            if (`Store.Active.Messages.Reactive.Count == 0`)
                <StackPanel HorizontalAlignment=Center VerticalAlignment=Center Padding=`new Thickness(16, 0, 16, 0)` Spacing=6 IsHitTestVisible=false>
                    <AppSymbolIcon Symbol=Folder FontSize=22 Foreground=`theme.TertiaryText` HorizontalAlignment=Center />
                    <TextBlock Text=`NewChatPath()` FontSize=13 Foreground=`theme.SecondaryText` TextAlignment=Center TextWrapping=Wrap
                               TextTrimming=`TextTrimming.CharacterEllipsis` MaxWidth=520 ToolTipService.ToolTip=`Store.ActiveDirectory().Length > 0 ? Store.ActiveDirectory() : Store.ServerDirectory` />
                </StackPanel>
        </Grid>
    </root>
    """)]
public partial class ChatMessageList : IQuickMarkupComponent<Grid>
{
    /// <summary>
    /// True while the user is pinned to the bottom of the message list; follow-the-stream
    /// autoscroll only runs in this state. Set by <see cref="OnScrollViewChanged"/> from any
    /// scroll (scrolling away from the bottom disables it, reaching the bottom re-enables it),
    /// and re-pinned by <see cref="ForceScrollToBottom"/> on explicit app actions (send,
    /// continue, undo, redo, permission).
    /// </summary>
    private bool _stickToBottom = true;

    /// <summary>Pixels from the very bottom that still count as "at the bottom" for stickiness.</summary>
    private const double StickToBottomThreshold = 40;

    /// <summary>
    /// The SessionStore whose Messages collection this component is currently hooked to. Hooking
    /// tracks the router's Active store so a session switch re-wires the CollectionChanged
    /// handler (and part hooks) to the newly-active store's collection.
    /// </summary>
    private SessionStore? _hookedStore;

    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();

        scrollHost.ViewChanged += OnScrollViewChanged;
        // Scrolling keyed off the message panel's laid-out size: SizeChanged fires after the
        // frame's layout pass, so ScrollableHeight reflects the freshly-rendered content
        // (new session messages, streaming parts). Scrolling earlier — right when a message is
        // added to the collection — targets a stale ScrollableHeight of 0 and leaves the
        // viewport at the top.
        messagePanel.SizeChanged += (_, _) => ScrollToBottom();
        // Messages live on the active SessionStore, which swaps on every session switch
        // (router keeps one cached store per session). Re-hook the CollectionChanged handler
        // and part hooks whenever the router's Active store changes.
        Store.ActiveStoreChanged += HookActiveStore;
        HookActiveStore();

        Store.ActivePermissionProp.Watch(_newReq =>
        {
            PermissionStage = "choose";
            RejectText = "";
            _ = ScrollToPermissionAsync();
        });

        // One-second tick that keeps the end-of-chat retry card's countdown live.
        var countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        countdown.Tick += (_, _) => Store.Active.UpdateRetryCountdown();
        countdown.Start();
    }

    /// <summary>Scrolls to the permission card once it has been added to the message list.</summary>
    private async Task ScrollToPermissionAsync()
    {
        await Task.Yield();
        ForceScrollToBottom();
    }

    private void HookActiveStore()
    {
        if (_hookedStore is not null)
            _hookedStore.Messages.CollectionChanged -= OnMessagesChanged;
        _hookedStore = Store.Active;
        _hookedStore.Messages.CollectionChanged += OnMessagesChanged;
        foreach (var message in _hookedStore.Messages) HookParts(message);
        // The markup foreach re-renders with the new collection; re-pin so the freshly-loaded
        // history autoscrolls into view.
        _stickToBottom = true;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A full list rebuild (session switch / new session / configure) restarts pinned to
        // the bottom; the freshly-loaded messages then autoscroll into view.
        if (e.Action == NotifyCollectionChangedAction.Reset)
            _stickToBottom = true;
        if (e.NewItems is not null)
            foreach (MessageItem message in e.NewItems) HookParts(message);
        ScrollToBottom();
    }

    private void HookParts(MessageItem message) =>
        message.Parts.CollectionChanged += (_, _) => ScrollToBottom();

    /// <summary>
    /// Resumes a turn that stopped with an error. Sends a "continue" user message — the agent
    /// is instructed (prompt/beast.txt) to pick up from the last incomplete step in its todo
    /// list. Matches the TUI, which has no separate continue API: it's just a user message.
    /// </summary>
    private async Task ContinueAsync()
    {
        await Store.Active.SendAsync("continue");
        ForceScrollToBottom();
    }

    /// <summary>Restore reverted messages (redo the undo), then scroll to the end.</summary>
    private async Task RedoLastMessageAsync()
    {
        await Store.Active.RedoLastMessageAsync();
        ForceScrollToBottom();
    }

    /// <summary>
    /// Revert to a specific user message (web/TUI parity): rewind the conversation to that
    /// message, restore its prompt into the composer, then scroll to the end.
    /// </summary>
    private async Task OnMessageRevertRequested(MessageItem message)
    {
        await Store.Active.RevertToMessageAsync(message);
        Input = Store.Active.RevertPromptText;
        ForceScrollToBottom();
    }

    /// <summary>
    /// Fork the conversation at a specific user message (web/TUI parity): create a new session
    /// containing the history up to that message, switch to it, restore the forked-at message's
    /// prompt into the composer, then scroll to the end.
    /// </summary>
    private async Task OnMessageForkRequested(MessageItem message)
    {
        await Store.ForkFromMessageAsync(message);
        Input = Store.Active.ForkPromptText;
        ForceScrollToBottom();
    }

    private async Task AllowPermissionOnceAsync()
    {
        var req = Store.ActivePermission;
        if (req is null) return;
        await Store.ReplyPermissionAsync(req.Id, "once");
    }

    private async Task AllowPermissionAlwaysAsync()
    {
        var req = Store.ActivePermission;
        if (req is null) return;
        await Store.ReplyPermissionAsync(req.Id, "always");
    }

    private void StartReject() => PermissionStage = "reject";

    private async Task RejectPermissionAsync()
    {
        var req = Store.ActivePermission;
        if (req is null) return;
        await Store.ReplyPermissionAsync(req.Id, "reject", RejectText.Trim());
    }

    private void CancelPermission() => PermissionStage = "choose";

    /// <summary>
    /// The active chat's folder, shown as a centered empty-state label in the chat body while
    /// there are no messages so the user knows which directory the session belongs to. Resolves
    /// the session's directory (or the pending folder for an unsaved draft), falling back to the
    /// server's directory, then displays it relative to the server directory — the same
    /// reference point the sidebar uses — via the shared <see cref="PathDisplay"/> helper.
    /// </summary>
    private string NewChatPath()
    {
        var dir = Store.ActiveDirectory();
        if (dir.Length == 0) dir = Store.ServerDirectory;
        if (dir.Length == 0) return "";
        return PathDisplay.Relative(dir, Store.ServerDirectory);
    }

    /// <summary>
    /// Follow-the-stream autoscroll: only runs while the user is pinned to the bottom, so a
    /// manual scroll-up leaves the viewport alone until the user scrolls back down to the
    /// bottom. The primary trigger is <c>messagePanel.SizeChanged</c>, which fires after the
    /// frame's layout pass — the moment ScrollableHeight reflects the newly-rendered content.
    /// </summary>
    private void ScrollToBottom()
    {
        if (scrollHost is null || !_stickToBottom) return;
        scrollHost.ChangeView(null, scrollHost.ScrollableHeight, null, true);
    }

    /// <summary>
    /// Explicit app-action scroll (send, continue, undo/redo, permission): re-pins the view
    /// to the bottom regardless of the user's current position, then autoscrolls.
    /// </summary>
    public void ForceScrollToBottom()
    {
        if (scrollHost is null) return;
        _stickToBottom = true;
        ScrollToBottom();
    }

    /// <summary>
    /// Tracks whether the user is pinned to the bottom. Every ViewChanged event is honored
    /// (including intermediate drag/inertia frames) so a scroll-up disables autoscroll
    /// immediately and a scroll-down to the bottom re-enables it. Our own programmatic
    /// scrolls use ChangeView with disableAnimation, which raises exactly one
    /// non-intermediate event at the bottom, so they never falsely unpin.
    /// </summary>
    private void OnScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (scrollHost is null) return;
        _stickToBottom = scrollHost.ScrollableHeight - scrollHost.VerticalOffset <= StickToBottomThreshold;
    }
}
