using UnoVibe.Controls.ToolViews;

namespace UnoVibe.Pages.Chat;

[QuickMarkup("""
    using UnoVibe.Controls.ToolViews;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Windows.UI.Text;
    required MessageItem Message;
    bool ShowHeader = true;
    bool PreviewMode = false;
    `IReadOnlySet<string>?` VisiblePartIds;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <Grid Margin=`new Thickness(0, 4, 0, 4)`>
        <StackPanel Spacing=4>
            if (`ShowHeader && !PreviewMode`)
                <TextBlock Text=`Message.Role == "user" ? "You" : "OpenCode"` FontSize=11
                            Foreground=`theme.SecondaryText` IsTextSelectionEnabled=true
                            HorizontalAlignment=`Message.Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left` />
            <StackPanel Spacing=6>
                foreach (var p in `Message.Parts`; `p.Id`)
                {
                    if (`VisiblePartIds is null || VisiblePartIds.Contains(p.Id)`)
                        <MessagePartView Part=`p` Message=`Message` PreviewMode=`PreviewMode` OnPartRevertRequested+=`OnPartRevertRequested` OnPartForkRequested+=`OnPartForkRequested` OnQuestionAnswered+=`OnQuestionAnswered` OnQuestionRejected+=`OnQuestionRejected` />
                }
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

    public event ToolViewQuestion.AnswerHandler? QuestionAnswered;

    public event ToolViewQuestion.RejectHandler? QuestionRejected;

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

    private Task OnQuestionAnswered(string requestId, IReadOnlyList<IReadOnlyList<string>> answers) =>
        QuestionAnswered?.Invoke(requestId, answers) ?? Task.CompletedTask;

    private Task OnQuestionRejected(string requestId) =>
        QuestionRejected?.Invoke(requestId) ?? Task.CompletedTask;

    private void RecomputeHeader(MessageItem msg) =>
        ShowHeader = !msg.Parts.Any(p => p.Type == "compaction");
}
