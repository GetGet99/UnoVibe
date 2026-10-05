namespace UnoVibe.Pages.Chat;

[QuickMarkup("""
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    using Windows.UI.Text;
    required TextPartItem Part;
    MessageItem? Message;
    bool PlainMode = false;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <StackPanel Spacing=4 HorizontalAlignment=`Message?.Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left`>
        <Border CornerRadius=8 Padding=`new Thickness(12, 8, 12, 8)` MaxWidth=720
                HorizontalAlignment=`Message?.Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left`
                Background=`Message?.Role == "user"
                    ? (theme.Accent is SolidColorBrush accent ? new SolidColorBrush(accent.Color) { Opacity = 0.3 } : theme.CardBackground)
                    : theme.CardBackground`
                BorderBrush=`theme.CardStroke` BorderThickness=`new Thickness(1)`>
            <MarkdownView Text=`Part.Text` PlainMode=`PlainMode` />
        </Border>
        <StackPanel Orientation=Horizontal Spacing=4 HorizontalAlignment=`Message?.Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left`>
            <Button Width=26 Height=22 Padding=0 CornerRadius=5 Background=`theme.SubtleFill` BorderThickness=0
                    ToolTipService.ToolTip=`PlainMode ? "Show formatted Markdown" : "Show plain text"`
                    @Click+=`PlainMode = !PlainMode`>
                <AppSymbolIcon Symbol=`PlainMode ? Symbol.Font : Symbol.Bullets` FontSize=11 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
            </Button>
            if (`Message?.Role == "user"`)
            {
                <Button Width=26 Height=22 Padding=0 CornerRadius=5 Background=`theme.SubtleFill` BorderThickness=0
                        ToolTipService.ToolTip="Fork conversation from this message"
                        @Click+=`await ForkFromHereAsync()`>
                    <AppSymbolIcon Symbol=`Symbol.PrivateCall` FontSize=11 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                </Button>
                <Button Width=26 Height=22 Padding=0 CornerRadius=5 Background=`theme.SubtleFill` BorderThickness=0
                        ToolTipService.ToolTip="Undo everything after this message"
                        Flyout=confirmFlyout = <Flyout Placement=BottomEdgeAlignedRight>
                    <StackPanel Spacing=8 MaxWidth=240 Padding=4>
                        <TextBlock Text="Undo everything after this message?" FontSize=13 FontWeight=`FontWeights.SemiBold` TextWrapping=Wrap />
                        <TextBlock Text="The conversation rewinds to this message and its prompt is restored to the input box." FontSize=11 Foreground=`theme.SecondaryText` TextWrapping=Wrap />
                        <StackPanel Orientation=Horizontal Spacing=8 HorizontalAlignment=Right>
                            <TextBlock Text="Click outside to cancel" FontSize=11 FontStyle=`FontStyle.Italic` Foreground=`theme.TertiaryText` VerticalAlignment=Center />
                            <Button Content="Undo" CornerRadius=6 Padding=`new Thickness(10,  4, 10,  4)` @Click+=`await RevertToHereAsync()` />
                        </StackPanel>
                    </StackPanel>
                </Flyout>>
                    <AppSymbolIcon Symbol=Undo FontSize=11 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                </Button>
            }
        </StackPanel>
    </StackPanel>
    """)]
partial class MessageTextPart : IQuickMarkupComponent
{
    public delegate Task RevertHandler(MessageItem message);

    public event RevertHandler? RevertRequested;

    public delegate Task ForkHandler(MessageItem message);

    public event ForkHandler? ForkRequested;

    [QuickMarkupConstructor]
    private void Ctor()
    {
        PlainMode = Message?.Role == "user";
        Init();
    }

    private async Task RevertToHereAsync()
    {
        if (confirmFlyout is { IsOpen: true }) confirmFlyout.Hide();
        if (Message is null) return;
        if (RevertRequested is { } handler) await handler(Message);
    }

    private async Task ForkFromHereAsync()
    {
        if (Message is null) return;
        if (ForkRequested is { } handler) await handler(Message);
    }
}
