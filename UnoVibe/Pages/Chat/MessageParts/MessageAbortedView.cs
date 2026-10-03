namespace UnoVibe.Pages.Chat.MessageParts;

[QuickMarkup("""
    using QuickMarkup.WinUI;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <Border Background=`theme.SystemCautionBackground` CornerRadius=4 Padding=`new Thickness(10,  6, 10,  6)` Margin=`new Thickness(0, 2, 0, 2)`>
        <StackPanel Orientation=Horizontal Spacing=6>
            <TextBlock Text="⏹" FontSize=12 Foreground=`theme.SystemCaution` VerticalAlignment=Center />
            <TextBlock Text="Interrupted by you — the response was stopped." FontSize=12 Foreground=`theme.SystemCaution` TextWrapping=Wrap IsTextSelectionEnabled=true VerticalAlignment=Center />
        </StackPanel>
    </Border>
    """)]
partial class MessageAbortedView : IQuickMarkupComponent;
