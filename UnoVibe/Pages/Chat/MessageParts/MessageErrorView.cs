namespace UnoVibe.Pages.Chat.MessageParts;

[QuickMarkup("""
    using UnoVibe.Controls.ToolViews;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Windows.UI.Text;
    required ErrorPartItem Part;
    required MessageItem Message;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <Border Background=`theme.SystemCriticalBackground` CornerRadius=4 Padding=`new Thickness(10,  6, 10,  6)` Margin=`new Thickness(0, 2, 0, 2)`
            BorderBrush=`theme.SystemCritical` BorderThickness=`new Thickness(1)` HorizontalAlignment=`Message.Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left` MaxWidth=640>
        <StackPanel Spacing=2>
            <TextBlock Text=`Part.ErrorName.Length > 0 ? Part.ErrorName : "Error"` FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.SystemCritical` IsTextSelectionEnabled=true />
            <TextBlock Text=`Part.ErrorMessage` FontSize=12 Foreground=`theme.PrimaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
        </StackPanel>
    </Border>
    """)]
partial class MessageErrorView : IQuickMarkupComponent;
