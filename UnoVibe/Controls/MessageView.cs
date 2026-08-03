using System.Collections.Specialized;
using UnoVibe.Models;

namespace UnoVibe.Controls;

/// <summary>
/// Renders a single chat message: a role header and the message parts.
/// User messages are right-aligned accent bubbles; assistant messages are left-aligned.
/// </summary>
[QuickMarkup("""
    using UnoVibe;
    using UnoVibe.Models;
    using UnoVibe.Controls.ToolViews;
    using QuickMarkup.WinUI;
    MessageItem? Message;
    bool ShowHeader = true;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <Grid Margin=`new Thickness(0, 4, 0, 4)`>
        if (`Message is not null`)
        {
            <StackPanel Spacing=4>
                if (`ShowHeader`)
                    <TextBlock Text=`Message.Role == "user" ? "You" : "OpenCode"` FontSize=11
                               Foreground=`theme.SecondaryText` IsTextSelectionEnabled=true
                               HorizontalAlignment=`Message.Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left` />
                <StackPanel Spacing=6>
                    foreach (var p in `Message.Parts`)
                    {
                        if (`p.Type == "text" && !p.Synthetic`)
                            <Border CornerRadius=8 Padding=`new Thickness(12, 8, 12, 8)` MaxWidth=720
                                    HorizontalAlignment=`Message.Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left`
                                    Background=`Message.Role == "user" ? theme.Accent : theme.CardBackground`
                                    BorderBrush=`theme.CardStroke` BorderThickness=`Message.Role == "user" ? new Thickness(0) : new Thickness(1)`>
                                <TextBlock Text=`p.Text` TextWrapping=Wrap IsTextSelectionEnabled=true
                                           Foreground=`Message.Role == "user" ? AppTheme.TextOnAccent : theme.PrimaryText` />
                            </Border>
                        else if (`p.Type == "compaction"`)
                            <Border BorderThickness=`new Thickness(0, 1, 0, 0)` BorderBrush=`theme.DividerStroke` Padding=`new Thickness(0, 6, 0, 6)` Margin=`new Thickness(0, 8, 0, 8)`>
                                <TextBlock Text="Compaction" FontSize=11 Foreground=`theme.SecondaryText`
                                           HorizontalAlignment=Center IsTextSelectionEnabled=true />
                            </Border>
                        else if (`p.Type == "reasoning"`)
                            <ToolViewReasoning Part=`p` />
                        else if (`p.Type == "step-start" || p.Type == "step-finish"`)
                            <TextBlock Text="" Visibility=Collapsed />
                        else if (`p.Type == "patch"`)
                            <Border Background=`theme.SubtleFill` CornerRadius=4 Padding=`new Thickness(8, 4, 8, 4)`>
                                <TextBlock Text=`p.Files.Length > 0 ? $"Edited {p.Files.Length} file(s): " + string.Join(", ", p.Files) : "file changes"` FontSize=12 TextWrapping=Wrap IsTextSelectionEnabled=true />
                            </Border>
                        else if (`p.Type == "file"`)
                            <Border Background=`theme.SubtleFill` CornerRadius=4 Padding=`new Thickness(8, 4, 8, 4)`>
                                <TextBlock Text=`p.FileName.Length > 0 ? $"file: {p.FileName}" : "file"` FontSize=12 TextWrapping=Wrap IsTextSelectionEnabled=true />
                            </Border>
                        else if (`p.Type == "tool"`)
                            if (`p.ToolName == "bash" || p.ToolName == "shell"`)
                                <ToolViewShell Part=`p` />
                            else if (`p.ToolName == "glob"`)
                                <ToolViewGlob Part=`p` />
                            else if (`p.ToolName == "grep"`)
                                <ToolViewGrep Part=`p` />
                            else if (`p.ToolName == "webfetch"`)
                                <ToolViewWebFetch Part=`p` />
                            else if (`p.ToolName == "skill"`)
                                <ToolViewSkill Part=`p` />
                            else if (`p.ToolName == "read"`)
                                <ToolViewRead Part=`p` />
                            else if (`p.ToolName == "edit"`)
                                <ToolViewEdit Part=`p` />
                            else if (`p.ToolName == "write"`)
                                <ToolViewWrite Part=`p` />
                            else if (`p.ToolName == "todowrite"`)
                                <ToolViewTodoWrite Part=`p` />
                            else if (`p.ToolName == "question"`)
                                <ToolViewQuestion Part=`p` />
                            else
                                <ToolViewGeneric Part=`p` />
                        else
                            <TextBlock Text=`$"[{p.Type}]"` FontSize=11 Foreground=`theme.TertiaryText` IsTextSelectionEnabled=true />
                    }
                </StackPanel>
            </StackPanel>
        }
    </Grid>
    """)]
public partial class MessageView : IQuickMarkupComponent
{
    [QuickMarkupConstructor]
    private void Ctor()
    {
        ShowHeader = true;
        var msg = Message;
        if (msg is not null)
        {
            RecomputeHeader(msg);
            msg.Parts.CollectionChanged += (_, _) => RecomputeHeader(msg);
        }
        Init();
    }

    private void RecomputeHeader(MessageItem msg) =>
        ShowHeader = !msg.Parts.Any(p => p.Type == "compaction");
}
