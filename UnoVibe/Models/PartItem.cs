using QuickMarkup.Infra.Collections;

namespace UnoVibe.Models;

public struct ReasoningTime
{
    public long Start;
    public long End;
    public bool IsDone => End > 0;
    public long DurationMs => End > 0 && End >= Start ? End - Start : 0;
}

[QuickRefs("""
    using Microsoft.UI.Xaml.Media.Imaging;
    public string Text = "";
    public string? ToolName;
    public string? ToolStatus;
    public string? ToolTitle;
    public bool Interrupted = false;
    public string ToolInput = "";
    public string ToolContent = "";
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
    public `List<UnoVibe.Integration.QuestionInfo>` Questions = `[]`;
    public string AnswerJson = "";
    public string PatchJson = "";
    public string QuestionRequestId = "";
    public string ToolSessionId = "";
    public string ToolParentSessionId = "";
    public string ToolSubagentType = "";
    public string ErrorName = "";
    public string ErrorMessage = "";
    public string Mime = "";
    public string Url = "";
    public BitmapImage? Image;
    public bool IsImage => `Mime.StartsWith("image/")`;
    public ReasoningTime Time;
    """)]
partial class PartItem
{
    public string Id { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string CallId { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Synthetic { get; set; }
    public string FileName { get; set; } = "";
    public string[] Files { get; set; } = Array.Empty<string>();
    public ReactiveList<QuestionFormItem> QuestionForm { get; } = new();

    public async Task LoadImageAsync()
    {
        if (Image is not null || !IsImage || !Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;
        var comma = Url.IndexOf(',');
        if (comma < 0) return;
        try
        {
            var bytes = Convert.FromBase64String(Url.Substring(comma + 1));
            Image = await ImageAttachment.DecodeAsync(bytes);
        }
        catch
        {
        }
    }
}
