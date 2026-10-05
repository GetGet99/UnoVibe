namespace UnoVibe.Pages.Chat.MessageParts;

[QuickMarkup("""
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    required ReasoningPartItem Part;
    bool Expanded = false;
    bool PlainMode = false;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <StackPanel Spacing=4 MaxWidth=720 HorizontalAlignment=Left>
        <AccordionHeader Title=`Part.Time.IsDone ? Part.ThoughtLabel : Part.Label` Expanded=`Expanded` Enabled=`Part.Summary.Body.Length > 0` SemiBold ShowSpinner=`!Part.Time.IsDone` SpinnerForeground=`theme.SystemCaution` TitleForeground=`Part.Time.IsDone ? theme.SecondaryText : theme.SystemCaution` Toggle=`() => Expanded = !Expanded` />
        if (`Expanded`)
        {
            if (`Part.Summary.Body.Length > 0`)
            {
                <Border Background=`theme.SolidBackground` CornerRadius=4 Padding=`new Thickness(8, 6, 8, 6)`>
                    <MarkdownView Text=`Part.Summary.Body` PlainMode=`PlainMode` />
                </Border>
                <Button Width=26 Height=22 Padding=0 CornerRadius=5 Background=`theme.SubtleFill` BorderThickness=0
                        HorizontalAlignment=Left
                        ToolTipService.ToolTip=`PlainMode ? "Show formatted Markdown" : "Show plain text"`
                        @Click+=`PlainMode = !PlainMode`>
                    <AppSymbolIcon Symbol=`PlainMode ? Symbol.Font : Symbol.Bullets` FontSize=11 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                </Button>
            }
        }
    </StackPanel>
    """)]
partial class MessageReasoningView : IQuickMarkupComponent;
