using UnoVibe.Services;

namespace UnoVibe.Pages.Chat;

/// <summary>
/// Chat page header: session title (with inline rename), back-to-parent button, busy ring,
/// folder actions, full-session fork button, the session stats flyout, and the compact
/// cost / tokens / context usage summary next to it.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Integration;
    using UnoVibe.Services;
    using UnoVibe.Controls;
    using UnoVibe.Providers;
    using QuickMarkup.WinUI;
    using Microsoft.UI;
    inject SessionsSource Sessions;
    inject bool IsCompact;
    inject bool IsSidebarView;
    inject OpencodeClient Opencode;
    inject ToastService Toasts;
    inject UIService UIs;
    bool EditingTitle = false;
    // Can be null if it's pending session to create
    SessionHead? Head => `Sessions.Head(Sessions.ActiveSessionId)`;

    bool IsSubagent => `Head?.IsSubagent ?? false`;
    bool IsBusy => `Head?.IsBusy ?? false`;

    string TitleEdit = "";
    <setup>
        var theme = ThemeBrushes.Global;
        var transparent = new SolidColorBrush(Colors.Transparent);
    </setup>
    <root>
        <Grid ColumnSpacing=`IsCompact ? 6 : 8` Padding=`new Thickness(IsCompact ? 12 : 16, 12, IsCompact ? 12 : 16, 8)`
              RowDefinitions=<>
                  <RowDefinition Height=Auto />
                  <RowDefinition Height=`IsCompact ? GridLength.Auto : new GridLength(0)` />
              </> ColumnDefinitions=<>
                  <ColumnDefinition />
                  <ColumnDefinition Width=Auto />
              </>>
            <StackPanel VerticalAlignment=Center>
                if (`EditingTitle`)
                {
                    <StackPanel Orientation=Horizontal Spacing=6 VerticalAlignment=Center>
                        titleEdit = <TextBox Text<=>`TitleEdit` MinWidth=220 FontSize=14 VerticalContentAlignment=Center KeyDown+=`OnTitleKeyDown` />
                        <Button Content="Save" @Click+=`SaveTitle()` Padding=`new Thickness(10,  4, 10,  4)` CornerRadius=6 />
                        <Button Content="Cancel" @Click+=`CancelTitleEdit()` Padding=`new Thickness(10,  4, 10,  4)` CornerRadius=6 />
                    </StackPanel>
                }
                else
                {
                    // Title row as a Grid so a long title truncates with an ellipsis instead of
                    // pushing the pencil off-screen: the title is a star column, so the trailing
                    // Auto columns (pencil, busy ring) always keep their room. The conditional
                    // leading buttons (hamburger, back-to-parent) sit in Auto columns that react
                    // to 0-width when absent, so they never leave phantom gaps.
                    <Grid ColumnDefinitions=<>
                        <ColumnDefinition Width=`IsCompact ? GridLength.Auto : new GridLength(0)` />
                        <ColumnDefinition Width=`IsSubagent ? GridLength.Auto : new GridLength(0)` />
                        <ColumnDefinition />
                        <ColumnDefinition Width=Auto />
                        <ColumnDefinition Width=`IsBusy ? GridLength.Auto : new GridLength(0)` />
                    </>>
                        if (`IsCompact`)
                            <Button Grid.Column=0 Background=`transparent` BorderThickness=0 Padding=`new Thickness(6,  2, 6,  2)` CornerRadius=6
                                    Margin=`new Thickness(0, 0, 8, 0)`
                                    Foreground=`theme.SecondaryText` VerticalAlignment=Center @Click+=`IsSidebarView = true`
                                    ToolTipService.ToolTip="Open session list">
                                <AppSymbolIcon Symbol=`Symbol.GlobalNavButton` FontSize=14 />
                            </Button>
                        if (`IsSubagent`)
                            <Button Grid.Column=1 Background=`transparent` BorderThickness=0 Padding=`new Thickness(6,  2, 6,  2)` CornerRadius=6
                                    Margin=`new Thickness(0, 0, 8, 0)`
                                    Foreground=`theme.SecondaryText` VerticalAlignment=Center @Click+=`Sessions.ActiveSessionId = Head?.ParentId`
                                    ToolTipService.ToolTip="Back to parent session">
                                <AppSymbolIcon Symbol=Back FontSize=14 />
                            </Button>
                        <TextBlock Grid.Column=2 Text=`Head?.Title ?? "New Chat"` FontSize=16 FontWeight=`FontWeights.SemiBold`
                                   TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                        <Button Grid.Column=3 Background=`transparent` BorderThickness=0 Padding=`new Thickness(6,  2, 6,  2)`
                                Margin=`new Thickness(8, 0, 0, 0)`
                                Foreground=`theme.SecondaryText` VerticalAlignment=Center @Click+=`StartTitleEdit()`>
                            <AppSymbolIcon Symbol=Edit FontSize=13 />
                        </Button>
                        <ProgressRing Grid.Column=4 Width=16 Height=16 IsActive=`IsBusy`
                                      Margin=`new Thickness(8, 0, 0, 0)`
                                      Visibility=`IsBusy ? Visibility.Visible : Visibility.Collapsed` VerticalAlignment=Center />
                    </Grid>
                }
            </StackPanel>
            <StackPanel Grid.Column=1 Orientation=Horizontal Spacing=4 VerticalAlignment=Center>
                <FolderActions Directory=`Sessions.ActiveSessionDirectory` ShowFileManager=true ShowNewSession=false />
                <Button Padding=`new Thickness(6, 4, 6, 4)` VerticalAlignment=Center
                        ToolTipService.ToolTip="Fork full session"
                        IsEnabled=`IsSubagent` @Click+=`UIs.ForkAndSwitchSession()`>
                    <AppSymbolIcon Symbol=`Symbol.PrivateCall` FontSize=11 />
                </Button>
                <ChatCost />
            </StackPanel>
            // On compact windows the cost/tokens/context summary moves to a second line (it's
            // important enough to keep visible) instead of the inline text on the stats button.
            if (`IsCompact`)
            {
                <ChatCostInline />
            }
        </Grid>
    </root>
    """)]
public partial class ChatHeader : IQuickMarkupComponent<Grid>
{
    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();
        UIs.BeginRenameAndFocusRequested += BeginRename;
    }

    /// <summary>Public entry into rename mode for the /rename built-in command (the pencil icon
    /// calls <see cref="StartTitleEdit"/> directly). No-op while already editing.</summary>
    public void BeginRename()
    {
        if (EditingTitle) return;
        StartTitleEdit();
    }

    private void StartTitleEdit()
    {
        TitleEdit = Head?.Title ?? "New Chat";
        EditingTitle = true;
        _ = FocusTitleEditAsync();
    }

    private void CancelTitleEdit() => EditingTitle = false;

    private async void SaveTitle()
    {
        EditingTitle = false;
        if (Head?.Id is {} id)
            try
            {
                await Opencode.UpdateSessionTitleAsync(id, new() { Title = TitleEdit });
            } catch (Exception ex)
            {
                Toasts.ShowError(ex.Message, "Rename failed");
            }
    }

    /// <summary>Focuses and selects the rename box once the reactive tree has materialized it.</summary>
    private async Task FocusTitleEditAsync()
    {
        await Task.Delay(16);
        if (titleEdit is null) return;
        titleEdit.Focus(FocusState.Programmatic);
        titleEdit.SelectAll();
    }

    private void OnTitleKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            SaveTitle();
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            CancelTitleEdit();
        }
    }
}
