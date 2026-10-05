namespace UnoVibe.Controls.ToolViews;

[QuickMarkup("""
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    required ToolCallPartItem Part;
    bool Expanded = false;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <StackPanel Spacing=4>
        <AccordionHeader Title=`Part.DisplayName` Expanded=`Expanded` Enabled=`Part.ToolContent is not null || Part.ToolOutput is not null` ShowSpinner=`Part.IsBusy` SpinnerForeground=`Part.ToolStatus == "pending" ? theme.SystemNeutral : theme.SystemCaution` TitleForeground=`theme.PrimaryText` Toggle=`() => Expanded = !Expanded` />
        if (`Expanded`)
        {
            if (`Part.ToolContent is not null`)
                <CodeView Text=`Part.ToolContent` FilePath=`Part.ToolFilePath` />
            else if (`Part.ToolOutput is not null`)
                <Border Background=`theme.SolidBackground` CornerRadius=4 Padding=`new Thickness(8, 6, 8, 6)`>
                    <TextBlock Text=`ToolViewShared.Truncate(Part.ToolOutput, 4000)` FontSize=12 FontFamily=`CodeFontsHelper.Current` TextWrapping=Wrap IsTextSelectionEnabled=true />
                </Border>
        }
        if (`Part.ToolError is not null`)
            <Border Background=`theme.SystemCriticalBackground` CornerRadius=4 Padding=`new Thickness(8, 6, 8, 6)`>
                <TextBlock Text=`Part.ToolError` FontSize=11 Foreground=`theme.SystemCritical` TextWrapping=Wrap IsTextSelectionEnabled=true />
            </Border>
    </StackPanel>
    """)]
partial class ToolViewWrite : IQuickMarkupComponent;