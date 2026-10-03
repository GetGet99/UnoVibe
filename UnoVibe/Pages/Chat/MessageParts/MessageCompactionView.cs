namespace UnoVibe.Pages.Chat.MessageParts;

[QuickMarkup("""
    using QuickMarkup.WinUI;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <Border BorderThickness=`new Thickness(0, 1, 0, 0)` BorderBrush=`theme.DividerStroke` Padding=`new Thickness(0, 6, 0, 6)` Margin=`new Thickness(0, 8, 0, 8)`>
        <TextBlock Text="Compaction" FontSize=11 Foreground=`theme.SecondaryText`
                    HorizontalAlignment=Center IsTextSelectionEnabled=true />
    </Border>
    """)]
partial class MessageCompactionView : IQuickMarkupComponent;
