namespace UnoVibe.Pages.Chat.MessageParts;

[QuickMarkup("""
    using QuickMarkup.WinUI;
    required FilePartItem Part;
    required MessageItem Message;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        if (`Part.IsImage`)
            <Border CornerRadius=6 Padding=2 MaxWidth=320 MinWidth=48 MinHeight=48
                    HorizontalAlignment=`Message.Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left`
                    Background=`Message.Role == "user" ? theme.Accent : theme.CardBackground`
                    BorderBrush=`theme.CardStroke` BorderThickness=`Message.Role == "user" ? new Thickness(0) : new Thickness(1)`>
                <Image Source=`Part.Image` MaxWidth=300 MaxHeight=300 Stretch=Uniform />
            </Border>
        else
            <Border Background=`theme.SubtleFill` CornerRadius=4 Padding=`new Thickness(8, 4, 8, 4)`>
                <TextBlock Text=`Part.FileName.Length > 0 ? $"file: {Part.FileName}" : "file"` FontSize=12 TextWrapping=Wrap IsTextSelectionEnabled=true />
            </Border>
    </root>
    """)]
partial class MessageFileView : IQuickMarkupComponent;
