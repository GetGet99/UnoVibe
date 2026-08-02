using UnoVibe.Models;

namespace UnoVibe.Controls;

/// <summary>
/// Renders a single chat message: a role header and the message parts.
/// User messages are right-aligned accent bubbles; assistant messages are left-aligned.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Models;
    using UnoVibe.Controls.ToolViews;
    using QuickMarkup.WinUI;
    MessageItem? Message;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <Grid Margin=`new Thickness(0, 4, 0, 4)`>
        if (`Message is not null`)
        {
            <StackPanel Spacing=4>
                <TextBlock Text=`Message.Role == "user" ? "You" : "OpenCode"` FontSize=11
                           Foreground=`theme.SecondaryText`
                           HorizontalAlignment=`Message.Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left` />
                <Border CornerRadius=8 Padding=`new Thickness(12, 8, 12, 8)` MaxWidth=720
                        HorizontalAlignment=`Message.Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left`
                        Background=`Message.Role == "user" ? theme.Accent : theme.CardBackground`
                        BorderBrush=`theme.CardStroke` BorderThickness=`Message.Role == "user" ? new Thickness(0) : new Thickness(1)`>
                    <StackPanel Spacing=6>
                        foreach (var p in `Message.Parts`)
                        {
                            if (`p.Type == "text"`)
                                <TextBlock Text=`p.Text` TextWrapping=Wrap
                                           Foreground=`Message.Role == "user" ? theme.AccentText : theme.PrimaryText` />
                            else if (`p.Type == "reasoning"`)
                                <TextBlock Text=`p.Text` TextWrapping=Wrap FontSize=12
                                           Foreground=`theme.SecondaryText` FontStyle=Italic />
                            else if (`p.Type == "tool"`)
                                if (`p.ToolName == "bash" || p.ToolName == "shell"`)
                                    <ToolViewShell Part=`p` />
                                else if (`p.ToolName == "glob"`)
                                    <ToolViewGlob Part=`p` />
                                else if (`p.ToolName == "read"`)
                                    <ToolViewRead Part=`p` />
                                else if (`p.ToolName == "edit"`)
                                    <ToolViewEdit Part=`p` />
                                else if (`p.ToolName == "write"`)
                                    <ToolViewWrite Part=`p` />
                                else
                                    <ToolViewGeneric Part=`p` />
                            else if (`p.Type == "step-start" || p.Type == "step-finish"`)
                                <TextBlock Text="" Visibility=Collapsed />
                            else if (`p.Type == "patch"`)
                                <Border Background=`theme.SubtleFill` CornerRadius=4 Padding=`new Thickness(8, 4, 8, 4)`>
                                    <TextBlock Text=`p.Files.Length > 0 ? $"Edited {p.Files.Length} file(s): " + string.Join(", ", p.Files) : "file changes"` FontSize=12 TextWrapping=Wrap />
                                </Border>
                            else if (`p.Type == "file"`)
                                <Border Background=`theme.SubtleFill` CornerRadius=4 Padding=`new Thickness(8, 4, 8, 4)`>
                                    <TextBlock Text=`p.FileName.Length > 0 ? $"file: {p.FileName}" : "file"` FontSize=12 TextWrapping=Wrap />
                                </Border>
                            else
                                <TextBlock Text=`$"[{p.Type}]"` FontSize=11 Foreground=`theme.TertiaryText` />
                        }
                    </StackPanel>
                </Border>
            </StackPanel>
        }
    </Grid>
    """)]
public partial class MessageView : IQuickMarkupComponent;
