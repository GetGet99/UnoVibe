namespace UnoVibe.Controls.ToolViews;

/// <summary>
/// Renders the busy spinner for a tool part: gray while the model is still
/// streaming the tool-call arguments ("pending"), caution while the tool is
/// executing ("running"). Renders nothing once the part is done. A fragment
/// so callers get 0 or 1 element.
/// </summary>
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
public partial class ToolBusyIndicator : IQuickMarkupFragmentComponent<UIElement>;
