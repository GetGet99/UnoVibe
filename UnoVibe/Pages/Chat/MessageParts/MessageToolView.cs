namespace UnoVibe.Pages.Chat.MessageParts;

[QuickMarkup("""
    using UnoVibe.Controls.ToolViews;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Windows.UI.Text;
    required ToolCallPartItem Part;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        if (`Part.ToolName == "bash" || Part.ToolName == "shell"`)
            <ToolViewShell Part=`Part` />
        else if (`Part.ToolName == "glob"`)
            <ToolViewGlob Part=`Part` />
        else if (`Part.ToolName == "grep"`)
            <ToolViewGrep Part=`Part` />
        else if (`Part.ToolName == "webfetch"`)
            <ToolViewWebFetch Part=`Part` />
        else if (`Part.ToolName == "skill"`)
            <ToolViewSkill Part=`Part` />
        else if (`Part.ToolName == "read"`)
            <ToolViewRead Part=`Part` />
        else if (`Part.ToolName == "edit"`)
            <ToolViewEdit Part=`Part` />
        else if (`Part.ToolName == "write"`)
            <ToolViewWrite Part=`Part` />
        else if (`Part.ToolName == "todowrite"`)
            <ToolViewTodoWrite Part=`Part` />
        else if (`Part.ToolName == "question"`)
            <ToolViewQuestion Part=`Part` />
        else if (`Part.ToolName == "task"`)
            <ToolViewTask Part=`Part` />
        else if (`Part.ToolName == "apply_patch"`)
            <ToolViewPatch Part=`Part` />
        else
            <ToolViewGeneric Part=`Part` />
    </root>
    """)]
partial class MessageToolView : IQuickMarkupComponent;
