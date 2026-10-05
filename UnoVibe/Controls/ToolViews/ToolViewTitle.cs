namespace UnoVibe.Controls.ToolViews;

[QuickMarkup("""
    using QuickMarkup.WinUI;
    required ToolCallPartItem Part;
    string Text = "";
    bool SemiBold = false;
    bool Emphasized = false;
    bool CodeFont = false;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        <StackPanel Orientation=Horizontal Spacing=8>
            <ToolBusyIndicator Part=`Part` />
            <TextBlock Text=`Text` FontSize=12
                       FontWeight=`SemiBold ? FontWeights.SemiBold : FontWeights.Normal`
                       FontFamily=`CodeFont ? CodeFontsHelper.Current : DefaultFont`
                       Foreground=`Emphasized ? theme.PrimaryText : theme.SecondaryText`
                       TextWrapping=Wrap IsTextSelectionEnabled=true VerticalAlignment=Center />
            if (`Part.Interrupted`)
                <Border Background=`theme.SystemCautionBackground` CornerRadius=4 Padding=`new Thickness(5, 1, 5, 2)` VerticalAlignment=Center>
                    <TextBlock Text="interrupted" FontSize=10 Foreground=`theme.SystemCaution` VerticalAlignment=Center />
                </Border>
        </StackPanel>
    </root>
    """)]
partial class ToolViewTitle : IQuickMarkupComponent<UIElement>
{
    public static FontFamily DefaultFont => new("Segoe UI Variable");
}
