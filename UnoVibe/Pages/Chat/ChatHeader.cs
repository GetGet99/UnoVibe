using UnoVibe.Services;

namespace UnoVibe.Pages.Chat;

/// <summary>
/// Chat page header: session title (with inline rename), back-to-parent button, busy ring,
/// folder actions, full-session fork button, the session stats flyout, and the compact
/// cost / tokens / context usage summary next to it.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Services;
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    using Microsoft.UI;
    inject ChatStore Store;
    bool EditingTitle = false;
    string TitleEdit = "";
    <setup>
        var theme = ThemeBrushes.Global;
        var transparent = new SolidColorBrush(Colors.Transparent);
    </setup>
    <root>
        <Grid ColumnSpacing=8 Padding=`new Thickness(16, 12, 16, 8)` ColumnDefinitions=<>
            <ColumnDefinition />
            <ColumnDefinition Width=Auto />
        </>>
            <StackPanel VerticalAlignment=Center>
                if (`EditingTitle`)
                {
                    <StackPanel Orientation=Horizontal Spacing=6 VerticalAlignment=Center>
                        titleEdit = <TextBox Text<=>`TitleEdit` MinWidth=220 FontSize=14 VerticalContentAlignment=Center KeyDown+=`OnTitleKeyDown` />
                        <Button Content="Save" @Click+=`await SaveTitleAsync()` Padding=`new Thickness(10,  4, 10,  4)` CornerRadius=6 />
                        <Button Content="Cancel" @Click+=`CancelTitleEdit()` Padding=`new Thickness(10,  4, 10,  4)` CornerRadius=6 />
                    </StackPanel>
                }
                else
                {
                    <StackPanel Orientation=Horizontal Spacing=8>
                        if (`Store.Active.ParentSessionId.Length > 0`)
                            <Button Background=`transparent` BorderThickness=0 Padding=`new Thickness(6,  2, 6,  2)` CornerRadius=6
                                    Foreground=`theme.SecondaryText` VerticalAlignment=Center @Click+=`await Store.GoToParentAsync()`
                                    ToolTipService.ToolTip="Back to parent session">
                                <AppSymbolIcon Symbol=Back FontSize=14 />
                            </Button>
                        <TextBlock Text=`Store.Active.SessionTitle` FontSize=16 FontWeight=`FontWeights.SemiBold` VerticalAlignment=Center />
                        <Button Background=`transparent` BorderThickness=0 Padding=`new Thickness(6,  2, 6,  2)` Foreground=`theme.SecondaryText` VerticalAlignment=Center @Click+=`StartTitleEdit()`>
                            <AppSymbolIcon Symbol=Edit FontSize=13 />
                        </Button>
                        <ProgressRing Width=16 Height=16 IsActive=`Store.Active.IsBusy`
                                      Visibility=`Store.Active.IsBusy ? Visibility.Visible : Visibility.Collapsed` VerticalAlignment=Center />
                    </StackPanel>
                }
            </StackPanel>
            <StackPanel Grid.Column=1 Orientation=Horizontal Spacing=4 VerticalAlignment=Center>
                <FolderActions Directory=`Store.ActiveDirectory()` ShowFileManager=true ShowNewSession=false />
                <Button Padding=`new Thickness(6,  4, 6,  4)` VerticalAlignment=Center
                        ToolTipService.ToolTip="Fork full session"
                        IsEnabled=`Store.ActiveSessionId.Length > 0` @Click+=`await Store.ForkFullSessionAsync()`>
                    <AppSymbolIcon Symbol=`Symbol.PrivateCall` FontSize=11 />
                </Button>
                <Button Background=`transparent` BorderThickness=0 Padding=`new Thickness(8,  2, 8,  2)` CornerRadius=6 VerticalAlignment=Center
                        ToolTipService.ToolTip="Session stats"
                        Flyout=<Flyout Placement=Bottom>
                    <StackPanel Spacing=8 MinWidth=260>
                        <TextBlock Text="Session stats" FontSize=13 FontWeight=`FontWeights.SemiBold` />
                        <Border Background=`theme.DividerStroke` Height=1 />
                        <Grid ColumnSpacing=12 ColumnDefinitions=<>
                            <ColumnDefinition Width=96 />
                            <ColumnDefinition />
                        </>>
                            <TextBlock Text="Cost" FontSize=12 Foreground=`theme.SecondaryText` />
                            <TextBlock Grid.Column=1 Text=`Store.Active.UsageCostLabel` FontSize=12 TextAlignment=Right VerticalAlignment=Center />
                        </Grid>
                        <TextBlock Text=`Store.SubagentCount > 0 ? "Tokens (excludes subagents)" : "Tokens"` FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.TertiaryText` />
                        <Grid ColumnSpacing=12 ColumnDefinitions=<>
                            <ColumnDefinition Width=96 />
                            <ColumnDefinition />
                        </>>
                            <TextBlock Text="Input*" FontSize=12 Foreground=`theme.SecondaryText` />
                            <TextBlock Grid.Column=1 Text=`Store.Active.UsageTokensInput.ToString("N0")` FontSize=12 TextAlignment=Right />
                        </Grid>
                        <Grid ColumnSpacing=12 ColumnDefinitions=<>
                            <ColumnDefinition Width=96 />
                            <ColumnDefinition />
                        </>>
                            <TextBlock Text="Output*" FontSize=12 Foreground=`theme.SecondaryText` />
                            <TextBlock Grid.Column=1 Text=`Store.Active.UsageTokensOutput.ToString("N0")` FontSize=12 TextAlignment=Right />
                        </Grid>
                        if (`Store.Active.UsageTokensReasoning > 0`)
                            <Grid ColumnSpacing=12 ColumnDefinitions=<>
                                <ColumnDefinition Width=96 />
                                <ColumnDefinition />
                            </>>
                                <TextBlock Text="Reasoning*" FontSize=12 Foreground=`theme.SecondaryText` />
                                <TextBlock Grid.Column=1 Text=`Store.Active.UsageTokensReasoning.ToString("N0")` FontSize=12 TextAlignment=Right />
                            </Grid>
                        if (`Store.Active.UsageTokensCacheRead > 0`)
                            <Grid ColumnSpacing=12 ColumnDefinitions=<>
                                <ColumnDefinition Width=96 />
                                <ColumnDefinition />
                            </>>
                                <TextBlock Text="Cache read*" FontSize=12 Foreground=`theme.SecondaryText` />
                                <TextBlock Grid.Column=1 Text=`Store.Active.UsageTokensCacheRead.ToString("N0")` FontSize=12 TextAlignment=Right />
                            </Grid>
                        if (`Store.Active.UsageTokensCacheWrite > 0`)
                            <Grid ColumnSpacing=12 ColumnDefinitions=<>
                                <ColumnDefinition Width=96 />
                                <ColumnDefinition />
                            </>>
                                <TextBlock Text="Cache write*" FontSize=12 Foreground=`theme.SecondaryText` />
                                <TextBlock Grid.Column=1 Text=`Store.Active.UsageTokensCacheWrite.ToString("N0")` FontSize=12 TextAlignment=Right />
                            </Grid>
                        <Grid ColumnSpacing=12 ColumnDefinitions=<>
                            <ColumnDefinition Width=96 />
                            <ColumnDefinition />
                        </>>
                            <TextBlock Text="Total" FontSize=12 FontWeight=`FontWeights.SemiBold` Foreground=`theme.SecondaryText` />
                            <TextBlock Grid.Column=1 Text=`Store.Active.UsageTokensLabel` FontSize=12 FontWeight=`FontWeights.SemiBold` TextAlignment=Right />
                        </Grid>
                        <TextBlock Text="*based on last message" FontSize=11 Foreground=`theme.TertiaryText` />
                        <TextBlock Text="Context" FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.TertiaryText` />
                        <Grid ColumnSpacing=12 ColumnDefinitions=<>
                            <ColumnDefinition Width=96 />
                            <ColumnDefinition />
                        </>>
                            <TextBlock Text="Used" FontSize=12 Foreground=`theme.SecondaryText` />
                            <TextBlock Grid.Column=1 Text=`Store.Active.UsageTokensLabel` FontSize=12 TextAlignment=Right />
                        </Grid>
                        <Grid ColumnSpacing=12 ColumnDefinitions=<>
                            <ColumnDefinition Width=96 />
                            <ColumnDefinition />
                        </>>
                            <TextBlock Text="Max" FontSize=12 Foreground=`theme.SecondaryText` />
                            <TextBlock Grid.Column=1 Text=`Store.Active.ContextLimit > 0 ? Store.Active.ContextLimit.ToString("N0") : "--"` FontSize=12 TextAlignment=Right />
                        </Grid>
                        <ProgressBar Value=`Store.Active.ContextUsage` Minimum=0 Maximum=100 Height=4 />
                    </StackPanel>
                </Flyout>>
                <StackPanel Orientation=Horizontal Spacing=8>
                    <TextBlock Text=`Store.Active.UsageCostLabel` FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                    <TextBlock Text="·" FontSize=12 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
                    <TextBlock Text=`Store.Active.UsageTokensLabel` FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                    <TextBlock Text="tokens" FontSize=11 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
                    <TextBlock Text="·" FontSize=12 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
                    <TextBlock Text=`Store.Active.ContextLabel` FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                    <TextBlock Text="ctx" FontSize=11 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
                    <ProgressBar Value=`Store.Active.ContextUsage` Minimum=0 Maximum=100 Width=70 Height=4 VerticalAlignment=Center />
                </StackPanel>
            </Button>
            </StackPanel>
        </Grid>
    </root>
    """)]
public partial class ChatHeader : IQuickMarkupComponent<Grid>
{
    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();
    }

    private void StartTitleEdit()
    {
        TitleEdit = Store.Active.SessionTitle;
        EditingTitle = true;
        _ = FocusTitleEditAsync();
    }

    private void CancelTitleEdit() => EditingTitle = false;

    private async Task SaveTitleAsync()
    {
        EditingTitle = false;
        await Store.Active.RenameSessionAsync(TitleEdit);
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
            _ = SaveTitleAsync();
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            CancelTitleEdit();
        }
    }
}
