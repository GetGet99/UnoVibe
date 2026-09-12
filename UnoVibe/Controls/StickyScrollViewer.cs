namespace UnoVibe.Controls;

[QuickMarkup("""
    UIElement? Child;
    <root>
        scrollHost = <ScrollViewer Content=`Child` />
    </root>
    """)]
partial class StickyScrollViewer : IQuickMarkupComponent<ScrollViewer>
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

    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();
        scrollHost.ViewChanged += OnScrollViewChanged;
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
        _stickToBottom = scrollHost.ScrollableHeight - scrollHost.VerticalOffset <= StickToBottomThreshold;
    }

    
    /// <summary>
    /// Explicit app-action scroll (send, continue, undo/redo, permission): re-pins the view
    /// to the bottom regardless of the user's current position, then autoscrolls.
    /// </summary>
    public void ForceScrollToBottom()
    {
        _stickToBottom = true;
        ScrollToBottomIfStick();
    }
    
    /// <summary>
    /// Follow-the-stream autoscroll: only runs while the user is pinned to the bottom, so a
    /// manual scroll-up leaves the viewport alone until the user scrolls back down to the
    /// bottom. The primary trigger is <c>messagePanel.SizeChanged</c>, which fires after the
    /// frame's layout pass — the moment ScrollableHeight reflects the newly-rendered content.
    /// </summary>
    public void ScrollToBottomIfStick()
    {
        if (!_stickToBottom) return;
        scrollHost.ChangeView(null, scrollHost.ScrollableHeight, null, true);
    }

}