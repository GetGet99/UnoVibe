namespace UnoVibe.Models;

public struct ReasoningTime
{
    public long Start;
    public long End;
    public bool IsDone => End > 0;
    public long DurationMs => End > 0 && End >= Start ? End - Start : 0;
}

/// <summary>
/// A reactive model for a message part. <c>Text</c> is a QuickMarkup reference so
/// streaming deltas can append without replacing the item in the collection.
/// </summary>
[QuickMarkup("""
    public string Text = "";
    public string? ToolName;
    public string? ToolStatus;
    public string? ToolTitle;
    public string ToolInput = "";
    public string ToolOutput = "";
    public string ToolError = "";
    public string ToolCommand = "";
    public string ToolFilePath = "";
    public string ToolPattern = "";
    public string ToolSearchPath = "";
    public string ToolInclude = "";
    public string ToolWorkdir = "";
    public string ToolUrl = "";
    public string ToolSkillName = "";
    public string ShellOutput = "";
    public string Diff = "";
    public string LoadedFiles = "";
    public string MatchCount = "";
    public string TodoJson = "";
    public string QuestionJson = "";
    public string AnswerJson = "";
    public string QuestionRequestId = "";
    public ReasoningTime Time;
    """)]
public partial class PartItem
{
    public string Id { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string CallId { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Synthetic { get; set; }
    public string FileName { get; set; } = "";
    public string[] Files { get; set; } = Array.Empty<string>();
    public ObservableCollection<QuestionFormItem> QuestionForm { get; } = new();
}
