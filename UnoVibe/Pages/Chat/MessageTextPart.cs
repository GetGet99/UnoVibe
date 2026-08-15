using UnoVibe.Models;

namespace UnoVibe.Pages.Chat;

/// <summary>
/// Renders one non-synthetic text part as a chat bubble with a per-part action row underneath:
/// a markdown/plain toggle (both roles) and a "revert to here" button (user messages only).
/// The toggle state is internal to this component, so it only affects this bubble. The bubble
/// and action row align right for user messages and left for assistant messages.
/// </summary>
[QuickMarkup("""
    using UnoVibe;
    using UnoVibe.Models;
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    using Windows.UI.Text;
    required PartItem Part;
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
public partial class MessageTextPart : IQuickMarkupComponent
{
    /// <summary>Handler for <see cref="RevertRequested"/>.</summary>
    public delegate Task RevertHandler(MessageItem message);

    /// <summary>
    /// Raised when the user clicks the per-message "revert to here" button under a user message.
    /// The subscriber performs the actual revert (ChatStore) and restores the prompt into the
    /// composer. Matches the web client's per-message revert action / TUI message dialog.
    /// </summary>
    public event RevertHandler? RevertRequested;

    /// <summary>Handler for <see cref="ForkRequested"/>.</summary>
    public delegate Task ForkHandler(MessageItem message);

    /// <summary>
    /// Raised when the user clicks the per-message "fork from here" button under a user message.
    /// The subscriber forks the conversation at that message (ChatStore), switches to the new
    /// session, and restores the prompt into the composer. Matches the web client / TUI fork.
    /// </summary>
    public event ForkHandler? ForkRequested;

    [QuickMarkupConstructor]
    private void Ctor()
    {
        // User messages default to plain (accent bubble); assistant messages to markdown.
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
