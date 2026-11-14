namespace UnoVibe.Pages.Chat.MessageParts;

[QuickMarkup("""
    using QuickMarkup.WinUI;
    required FilePartItem Part;
    required MessageItem Message;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <Border>
        if (`Part.IsImage`)
            <Border CornerRadius=6 Padding=2 MaxWidth=320 MinWidth=48 MinHeight=48
                    HorizontalAlignment=`Message.Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left`
                    Background=`Message.Role == "user"
                        ? (theme.Accent is SolidColorBrush accent ? new SolidColorBrush(accent.Color) { Opacity = 0.3 } : theme.CardBackground)
                        : theme.CardBackground`
                    BorderBrush=`theme.CardStroke` BorderThickness=`Message.Role == "user" ? new Thickness(0) : new Thickness(1)`>
                <Image Source=`Part.Image` MaxWidth=300 MaxHeight=300 Stretch=Uniform />
            </Border>
        else
            <Border CornerRadius=6 Padding=`new Thickness(10, 6, 10, 6)` MaxWidth=480
                    HorizontalAlignment=`Message.Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left`
                    Background=`Message.Role == "user"
                        ? (theme.Accent is SolidColorBrush accent ? new SolidColorBrush(accent.Color) { Opacity = 0.3 } : theme.CardBackground)
                        : theme.CardBackground`
                    BorderBrush=`theme.CardStroke` BorderThickness=`Message.Role == "user" ? new Thickness(0) : new Thickness(1)`>
                <StackPanel Orientation=Horizontal Spacing=8>
                    <SymbolIcon Symbol=Document Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                    <TextBlock Text=`Part.FileName.Length > 0 ? Part.FileName : "file"` FontSize=12 Foreground=`theme.PrimaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
                </StackPanel>
            </Border>
    </Border>
    """)]
partial class MessageFileView : IQuickMarkupComponent;
