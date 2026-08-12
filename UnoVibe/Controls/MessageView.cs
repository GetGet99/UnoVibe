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
    using QuickMarkup.Infra.Collections;
    using Windows.UI.Text;
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
                            {
                                // Skip whitespace-only text parts entirely (no element rendered).
                                if (`p.Text.Trim().Length > 0`)
                                    <MessageTextPart Part=`p` Message=`Message` RevertRequested+=`OnPartRevertRequested` ForkRequested+=`OnPartForkRequested` />
                            }
                        else if (`p.Type == "aborted"`)
                            <Border Background=`theme.SystemCautionBackground` CornerRadius=4 Padding=`new Thickness(10, 6)` Margin=`new Thickness(0, 2, 0, 2)`>
                                <StackPanel Orientation=Horizontal Spacing=6>
                                    <TextBlock Text="⏹" FontSize=12 Foreground=`theme.SystemCaution` VerticalAlignment=Center />
                                    <TextBlock Text="Interrupted by you — the response was stopped." FontSize=12 Foreground=`theme.SystemCaution` TextWrapping=Wrap IsTextSelectionEnabled=true VerticalAlignment=Center />
                                </StackPanel>
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
                            if (`p.IsImage`)
                                <Border CornerRadius=6 Padding=2 MaxWidth=320 MinWidth=48 MinHeight=48
                                        HorizontalAlignment=`Message.Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left`
                                        Background=`Message.Role == "user" ? theme.Accent : theme.CardBackground`
                                        BorderBrush=`theme.CardStroke` BorderThickness=`Message.Role == "user" ? new Thickness(0) : new Thickness(1)`>
                                    <Image Source=`p.Image` MaxWidth=300 MaxHeight=300 Stretch=Uniform />
                                </Border>
                            else
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
                            else if (`p.ToolName == "task"`)
                                <ToolViewTask Part=`p` />
                            else if (`p.ToolName == "apply_patch"`)
                                <ToolViewPatch Part=`p` />
                            else
                                <ToolViewGeneric Part=`p` />
                        else if (`p.Type == "error"`)
                            <Border Background=`theme.SystemCriticalBackground` CornerRadius=4 Padding=`new Thickness(10, 6)` Margin=`new Thickness(0, 2, 0, 2)`
                                    BorderBrush=`theme.SystemCritical` BorderThickness=`new Thickness(1)` HorizontalAlignment=`Message.Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left` MaxWidth=640>
                                <StackPanel Spacing=2>
                                    <TextBlock Text=`p.ErrorName.Length > 0 ? p.ErrorName : "Error"` FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.SystemCritical` IsTextSelectionEnabled=true />
                                    <TextBlock Text=`p.ErrorMessage` FontSize=12 Foreground=`theme.PrimaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
                                </StackPanel>
                            </Border>
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
    /// <summary>Handler for <see cref="RevertRequested"/>.</summary>
    public delegate Task RevertHandler(MessageItem message);

    /// <summary>
    /// Raised when the user clicks the per-message "revert to here" button under a user message.
    /// The subscriber performs the actual revert (ChatStore) and restores the prompt into the
    /// composer. Matches the web client's per-message revert action / TUI message dialog.
    /// </summary>
    public event RevertHandler? RevertRequested;

    /// <summary>Handler for <see cref="ForkRequested"/>.</summary>
    public delegate Task ForkHandler(MessageItem message);

    /// <summary>
    /// Raised when the user clicks the per-message "fork from here" button under a user message.
    /// The subscriber forks the conversation at that message (ChatStore), switches to the new
    /// session, and restores the prompt into the composer. Matches the web client / TUI fork.
    /// </summary>
    public event ForkHandler? ForkRequested;

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

    /// <summary>Forwards a per-part revert request (from <see cref="MessageTextPart"/>) to <see cref="RevertRequested"/>.</summary>
    private Task OnPartRevertRequested(MessageItem message) =>
        RevertRequested?.Invoke(message) ?? Task.CompletedTask;

    /// <summary>Forwards a per-part fork request (from <see cref="MessageTextPart"/>) to <see cref="ForkRequested"/>.</summary>
    private Task OnPartForkRequested(MessageItem message) =>
        ForkRequested?.Invoke(message) ?? Task.CompletedTask;

    private void RecomputeHeader(MessageItem msg) =>
        ShowHeader = !msg.Parts.Any(p => p.Type == "compaction");
}
