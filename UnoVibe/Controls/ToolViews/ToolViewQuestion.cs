namespace UnoVibe.Controls.ToolViews;

[QuickMarkup("""
    using UnoVibe.Controls.ToolViews;
    using UnoVibe.States;
    using QuickMarkup.WinUI;
    required ToolCallPartItem Part;
    inject ChatMessagesState? ChatState;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <StackPanel Spacing=6>
        <ToolViewTitle Part=`Part` Text=`Part.DisplayName` />
        if (`Part.Answers.Count > 0`)
        {
            foreach (index; var q in `Part.Questions`)
            {
                <StackPanel Spacing=2>
                    <TextBlock Text=`q.Question` FontSize=12 Foreground=`theme.SecondaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
                    <TextBlock Text=`index < Part.Answers.Count ? string.Join(", ", Part.Answers[index]) : ""` FontSize=12 TextWrapping=Wrap IsTextSelectionEnabled=true
                               Padding=`new Thickness(8, 0, 0, 0)` />
                </StackPanel>
            }
        }
        else if (`Part.QuestionRequestId.Length > 0 && Part.QuestionForm.Count > 0 && Part.IsBusy`)
        {
            foreach (var q in `Part.QuestionForm`)
                <ToolViewQuestionItem Q=`q` />
            <StackPanel Orientation=Horizontal Spacing=8>
                <Button Content="Submit answers" @Click+=`await SubmitAnswersAsync()` />
                <Button Content="Reject" @Click+=`await RejectAsync()` />
            </StackPanel>
        }
        else
        {
            foreach (var q in `Part.Questions`)
            {
                <StackPanel Spacing=2>
                    <TextBlock Text=`q.Question` FontSize=12 TextWrapping=Wrap IsTextSelectionEnabled=true />
                </StackPanel>
            }
            if (`Part.ToolStatus == "error" && Part.ToolError.Length > 0`)
                <TextBlock Text=`Part.ErrorText` FontSize=11 Foreground=`theme.SecondaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
        }
    </StackPanel>
    """)]
public partial class ToolViewQuestion : IQuickMarkupComponent
{
    private async Task SubmitAnswersAsync()
    {
        var answers = new List<List<string>>();
        foreach (var q in Part.QuestionForm)
        {
            var selected = q.Options.Where(o => o.IsSelected).Select(o => o.Label).ToList();
            if (q.CustomSelected)
            {
                var custom = q.CustomText.Trim();
                if (custom.Length > 0) selected.Add(custom);
            }
            if (!q.Multiple) selected = selected.Take(1).ToList();
            answers.Add(selected);
        }

        if (Part.QuestionRequestId.Length == 0 || answers.Count == 0) return;
        if (ChatState is not null)
            await ChatState.ReplyQuestionAsync(Part.QuestionRequestId, answers);
    }

    private async Task RejectAsync()
    {
        if (Part.QuestionRequestId.Length == 0) return;
        if (ChatState is not null)
            await ChatState.RejectQuestionAsync(Part.QuestionRequestId);
    }
}
