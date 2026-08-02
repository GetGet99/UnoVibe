using System.Collections.ObjectModel;

namespace UnoVibe.Models;

/// <summary>Reactive state for one question in an inline question-tool form.</summary>
[QuickMarkup("""
    public string Question = "";
    public string Header = "";
    public string CustomText = "";
    public bool AllowCustom = true;
    public bool Multiple = false;
    public bool Answered = false;
    public string AnswerText = "";
    """)]
public partial class QuestionFormItem
{
    public ObservableCollection<QuestionOptionItem> Options { get; } = new();
}

[QuickMarkup("""
    public string Label = "";
    public string Description = "";
    public bool IsSelected = false;
    """)]
public partial class QuestionOptionItem;
