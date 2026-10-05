namespace UnoVibe.Controls;

[QuickMarkup("""
    UIElement? Child;
    <root>
        scrollHost = <ScrollViewer Content=`Child` />
    </root>
    """)]
partial class StickyScrollViewer : IQuickMarkupComponent<ScrollViewer>
{

    private bool _stickToBottom = true;

    private const double StickToBottomThreshold = 40;

    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();
        scrollHost.ViewChanged += OnScrollViewChanged;
    }

    private void OnScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        _stickToBottom = scrollHost.ScrollableHeight - scrollHost.VerticalOffset <= StickToBottomThreshold;
    }

    public void ForceScrollToBottom()
    {
        _stickToBottom = true;
        ScrollToBottomIfStick();
    }

    public void ScrollToBottomIfStick()
    {
        if (!_stickToBottom) return;
        scrollHost.ChangeView(null, scrollHost.ScrollableHeight, null, true);
    }

}