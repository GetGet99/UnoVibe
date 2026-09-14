namespace UnoVibe.Pages.Chat;

/// <summary>
/// Renders a single chat message: a role header and the message parts.
/// User messages are right-aligned accent bubbles; assistant messages are left-aligned.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Controls.ToolViews;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Windows.UI.Text;
    required MessageItem Message;
    bool ShowHeader = true;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <Grid Margin=`new Thickness(0, 4, 0, 4)`>
        <StackPanel Spacing=4>
            if (`ShowHeader`)
                <TextBlock Text=`Message.Role == "user" ? "You" : "OpenCode"` FontSize=11
                            Foreground=`theme.SecondaryText` IsTextSelectionEnabled=true
                            HorizontalAlignment=`Message.Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left` />
            <StackPanel Spacing=6>
                foreach (var p in `Message.Parts`)
                    <MessagePartView Part=`p` Message=`Message` />
            </StackPanel>
        </StackPanel>
    </Grid>
    """)]
public partial class MessageView : IQuickMarkupComponent
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
        ShowHeader = true;
        var msg = Message;
        RecomputeHeader(Message);
        msg.Parts.CollectionChanged += (_, _) => RecomputeHeader(msg);
        Init();
    }

    /// <summary>Forwards a per-part revert request (from <see cref="MessageTextPart"/>) to <see cref="RevertRequested"/>.</summary>
    private Task OnPartRevertRequested(MessageItem message) =>
        RevertRequested?.Invoke(message) ?? Task.CompletedTask;

    /// <summary>Forwards a per-part fork request (from <see cref="MessageTextPart"/>) to <see cref="ForkRequested"/>.</summary>
    private Task OnPartForkRequested(MessageItem message) =>
        ForkRequested?.Invoke(message) ?? Task.CompletedTask;

    private void RecomputeHeader(MessageItem msg) =>
        ShowHeader = !msg.Parts.Any(p => p.Type == "compaction");
}
