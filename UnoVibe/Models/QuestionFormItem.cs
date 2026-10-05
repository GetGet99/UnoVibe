namespace UnoVibe.Models;

[QuickRefs("""
    public string Question = "";
    public string Header = "";
    public string CustomText = "";
    public bool CustomSelected = false;
    public bool AllowCustom = true;
    public bool Multiple = false;
    public bool Answered = false;
    public string AnswerText = "";
    """)]
partial class QuestionFormItem
{
    public ObservableCollection<QuestionOptionItem> Options { get; } = new();
}

[QuickRefs("""
    public string Label = "";
    public string Description = "";
    public bool IsSelected = false;
    """)]
partial class QuestionOptionItem;
