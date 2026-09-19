namespace UnoVibe.Controls.ToolViews;

[QuickMarkup("""
    using QuickMarkup.WinUI;
    required ToolCallPartItem Part;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <StackPanel Spacing=4>
        <ToolViewTitle Part=`Part` Text=`Part.DisplayName` />
        if (`Part.LoadedFiles.Length > 0`)
            <TextBlock Text=`ToolViewShared.Truncate(Part.LoadedFilesText, 2000)` FontSize=11 FontFamily=`CodeFontsHelper.Current` Foreground=`theme.SecondaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
        if (`Part.ToolError.Length > 0`)
            <TextBlock Text=`Part.ToolError` FontSize=11 Foreground=`theme.SystemCritical` TextWrapping=Wrap IsTextSelectionEnabled=true />
    </StackPanel>
    """)]
public partial class ToolViewRead : IQuickMarkupComponent;
