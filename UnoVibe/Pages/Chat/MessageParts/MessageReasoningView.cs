namespace UnoVibe.Pages.Chat.MessageParts;

[QuickMarkup("""
    using UnoVibe.Controls.ToolViews;
    using QuickMarkup.WinUI;
    required ReasoningPartItem Part;
    <root>
        <ToolViewReasoning Part=`Part` />
    </root>
    """)]
partial class MessageReasoningView : IQuickMarkupComponent;
