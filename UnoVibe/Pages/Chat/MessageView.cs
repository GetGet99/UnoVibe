namespace UnoVibe.Pages.Chat;

[QuickMarkup("""
    using UnoVibe.Controls.ToolViews;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Windows.UI.Text;
    required MessageItem Message;
    bool ShowHeader = true;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <Grid Margin=`new Thickness(0, 4, 0, 4)`>
        <StackPanel Spacing=4>
            if (`ShowHeader`)
                <TextBlock Text=`Message.Role == "user" ? "You" : "OpenCode"` FontSize=11
                            Foreground=`theme.SecondaryText` IsTextSelectionEnabled=true
                            HorizontalAlignment=`Message.Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left` />
            <StackPanel Spacing=6>
                foreach (var p in `Message.Parts`)
                    <MessagePartView Part=`p` Message=`Message` OnPartRevertRequested+=`OnPartRevertRequested` OnPartForkRequested+=`OnPartForkRequested` />
            </StackPanel>
        </StackPanel>
    </Grid>
    """)]
partial class MessageView : IQuickMarkupComponent
{
    public delegate Task RevertHandler(MessageItem message);

    public event RevertHandler? RevertRequested;

    public delegate Task ForkHandler(MessageItem message);

    public event ForkHandler? ForkRequested;

    [QuickMarkupConstructor]
    private void Ctor()
    {
        ShowHeader = true;
        var msg = Message;
        RecomputeHeader(Message);
        msg.Parts.CollectionChanged += (_, _) => RecomputeHeader(msg);
        Init();
    }

    private Task OnPartRevertRequested(MessageItem message) =>
        RevertRequested?.Invoke(message) ?? Task.CompletedTask;

    private Task OnPartForkRequested(MessageItem message) =>
        ForkRequested?.Invoke(message) ?? Task.CompletedTask;

    private void RecomputeHeader(MessageItem msg) =>
        ShowHeader = !msg.Parts.Any(p => p.Type == "compaction");
}
