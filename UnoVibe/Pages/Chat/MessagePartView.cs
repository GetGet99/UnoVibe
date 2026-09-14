namespace UnoVibe.Pages.Chat;

[QuickMarkup("""
    using UnoVibe.Controls.ToolViews;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Windows.UI.Text;
    required ChatPartItem Part;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        if (`Part.Type == "text" && !Part.Synthetic`)
        {
            // Skip whitespace-only text parts entirely (no element rendered).
            if (`Part.Text.Trim().Length > 0`)
                <MessageTextPart Part=`Part` Message=`Message` RevertRequested+=`OnPartRevertRequested` ForkRequested+=`OnPartForkRequested` />
        }
        else if (`Part.Type == "aborted"`)
            <Border Background=`theme.SystemCautionBackground` CornerRadius=4 Padding=`new Thickness(10,  6, 10,  6)` Margin=`new Thickness(0, 2, 0, 2)`>
                <StackPanel Orientation=Horizontal Spacing=6>
                    <TextBlock Text="⏹" FontSize=12 Foreground=`theme.SystemCaution` VerticalAlignment=Center />
                    <TextBlock Text="Interrupted by you — the response was stopped." FontSize=12 Foreground=`theme.SystemCaution` TextWrapping=Wrap IsTextSelectionEnabled=true VerticalAlignment=Center />
                </StackPanel>
            </Border>
        else if (`Part.Type == "compaction"`)
            <Border BorderThickness=`new Thickness(0, 1, 0, 0)` BorderBrush=`theme.DividerStroke` Padding=`new Thickness(0, 6, 0, 6)` Margin=`new Thickness(0, 8, 0, 8)`>
                <TextBlock Text="Compaction" FontSize=11 Foreground=`theme.SecondaryText`
                            HorizontalAlignment=Center IsTextSelectionEnabled=true />
            </Border>
        else if (`Part.Type == "reasoning"`)
            <ToolViewReasoning Part=`Part` />
        else if (`Part.Type == "step-start" || Part.Type == "step-finish"`)
            <TextBlock Text="" Visibility=Collapsed />
        else if (`Part.Type == "patch"`)
            <Border Background=`theme.SubtleFill` CornerRadius=4 Padding=`new Thickness(8, 4, 8, 4)`>
                <TextBlock Text=`Part.Files.Length > 0 ? $"Edited {Part.Files.Length} file(s): " + string.Join(", ", Part.Files) : "file changes"` FontSize=12 TextWrapping=Wrap IsTextSelectionEnabled=true />
            </Border>
        else if (`Part.Type == "file"`)
            if (`Part.IsImage`)
                <Border CornerRadius=6 Padding=2 MaxWidth=320 MinWidth=48 MinHeight=48
                        HorizontalAlignment=`Message.Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left`
                        Background=`Message.Role == "user" ? theme.Accent : theme.CardBackground`
                        BorderBrush=`theme.CardStroke` BorderThickness=`Message.Role == "user" ? new Thickness(0) : new Thickness(1)`>
                    <Image Source=`Part.Image` MaxWidth=300 MaxHeight=300 Stretch=Uniform />
                </Border>
            else
                <Border Background=`theme.SubtleFill` CornerRadius=4 Padding=`new Thickness(8, 4, 8, 4)`>
                    <TextBlock Text=`Part.FileName.Length > 0 ? $"file: {Part.FileName}" : "file"` FontSize=12 TextWrapping=Wrap IsTextSelectionEnabled=true />
                </Border>
        else if (`Part.Type == "tool"`)
            <MessageToolView Part=`(ToolCallPartItem)Part` />
        else if (`Part.Type == "error"`)
            <MessageErrorView Part=`(ErrorPartItem)Part` />
        else
            <TextBlock Text=`$"[{Part.Type}]"` FontSize=11 Foreground=`theme.TertiaryText` IsTextSelectionEnabled=true />
    </root>
    """)]
partial class MessagePartView : IQuickMarkupFragmentComponent {

}