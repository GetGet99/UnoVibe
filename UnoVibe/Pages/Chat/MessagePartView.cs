namespace UnoVibe.Pages.Chat;

[QuickMarkup("""
    using UnoVibe.Pages.Chat.MessageParts;
    using UnoVibe.Controls.ToolViews;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Windows.UI.Text;
    required ChatPartItem Part;
    required MessageItem Message;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        if (`Part.Type == "text" && !((TextPartItem)Part).Synthetic`)
        {
            if (`((TextPartItem)Part).Text.Trim().Length > 0`)
                <MessageTextPart Part=`(TextPartItem)Part` Message=`Message` RevertRequested+=`x => OnPartRevertRequested?.Invoke(x)` ForkRequested+=`x => OnPartForkRequested?.Invoke(x)` />
        }
        else if (`Part.Type == "aborted"`)
            <MessageAbortedView />
        else if (`Part.Type == "compaction"`)
            <MessageCompactionView />
        else if (`Part.Type == "reasoning"`)
            <MessageReasoningView Part=`(ReasoningPartItem)Part` />
        else if (`Part.Type == "step-start" || Part.Type == "step-finish"`)
            <MessageStepView Part=`Part` />
        else if (`Part.Type == "patch"`)
            <MessagePatchView Part=`(PatchPartItem)Part` />
        else if (`Part.Type == "file"`)
            <MessageFileView Part=`(FilePartItem)Part` Message=`Message` />
        else if (`Part.Type == "tool"`)
            <MessageToolView Part=`(ToolCallPartItem)Part` />
        else if (`Part.Type == "error"`)
            <MessageErrorView Part=`(ErrorPartItem)Part` Message=`Message` />
        else
            <TextBlock Text=`$"[{Part.Type}]"` FontSize=11 Foreground=`theme.TertiaryText` IsTextSelectionEnabled=true />
    </root>
    """)]
partial class MessagePartView : IQuickMarkupFragmentComponent
{
    public event MessageTextPart.RevertHandler? OnPartRevertRequested;
    public event MessageTextPart.ForkHandler? OnPartForkRequested;
}
