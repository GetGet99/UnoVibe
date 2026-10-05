namespace UnoVibe.Controls.ToolViews;

[QuickMarkup("""
    using QuickMarkup.WinUI;
    required ToolCallPartItem Part;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        if (`Part.IsBusy`)
            <ProgressRing Width=14 Height=14 IsActive=true Foreground=`Part.ToolStatus == "pending" ? theme.SystemNeutral : theme.SystemCaution` VerticalAlignment=Center />
    </root>
    """)]
partial class ToolBusyIndicator : IQuickMarkupFragmentComponent<UIElement>;
