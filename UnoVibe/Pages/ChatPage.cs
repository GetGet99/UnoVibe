using System.Collections.Specialized;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using UnoVibe.Controls;
using UnoVibe.Models;
using UnoVibe.Services;

namespace UnoVibe.Pages;

[QuickMarkup("""
    using UnoVibe.Models;
    using UnoVibe.Services;
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    using Microsoft.UI;
    inject ChatStore Store;
    string Input = "";
    string PermissionStage = "choose";
    string RejectText = "";
    bool EditingTitle = false;
    string TitleEdit = "";
    <setup>
        var theme = ThemeBrushes.Global;
        var transparent = new SolidColorBrush(Colors.Transparent);
    </setup>
    <root>
        <Grid RowDefinitions=<>
            <RowDefinition Height=Auto />
            <RowDefinition Height=Auto />
            <RowDefinition />
            <RowDefinition Height=Auto />
            <RowDefinition Height=Auto />
            <RowDefinition Height=Auto />
        </>>
            <Grid Grid.Row=0 ColumnSpacing=8 Padding=`new Thickness(16, 12, 16, 8)` ColumnDefinitions=<>
                <ColumnDefinition />
                <ColumnDefinition Width=Auto />
            </>>
                <StackPanel VerticalAlignment=Center>
                    if (`EditingTitle`)
                    {
                        <StackPanel Orientation=Horizontal Spacing=6 VerticalAlignment=Center>
                            titleEdit = <TextBox Text<=>`TitleEdit` MinWidth=220 FontSize=14 VerticalContentAlignment=Center KeyDown+=`OnTitleKeyDown` />
                            <Button Content="Save" @Click+=`await SaveTitleAsync()` Padding=`new Thickness(10, 4)` CornerRadius=6 />
                            <Button Content="Cancel" @Click+=`CancelTitleEdit()` Padding=`new Thickness(10, 4)` CornerRadius=6 />
                        </StackPanel>
                    }
                    else
                    {
                        <StackPanel Orientation=Horizontal Spacing=8>
                            if (`Store.ParentSessionId.Length > 0`)
                                <Button Background=`transparent` BorderThickness=0 Padding=`new Thickness(6, 2)` CornerRadius=6
                                        Foreground=`theme.SecondaryText` VerticalAlignment=Center @Click+=`await Store.GoToParentAsync()`
                                        ToolTipService.ToolTip="Back to parent session">
                                    <AppSymbolIcon Symbol=Back FontSize=14 />
                                </Button>
                            <TextBlock Text=`Store.SessionTitle` FontSize=16 FontWeight=`FontWeights.SemiBold` VerticalAlignment=Center />
                            <Button Background=`transparent` BorderThickness=0 Padding=`new Thickness(6, 2)` Foreground=`theme.SecondaryText` VerticalAlignment=Center @Click+=`StartTitleEdit()`>
                                <AppSymbolIcon Symbol=Edit FontSize=13 />
                            </Button>
                            <ProgressRing Width=16 Height=16 IsActive=`Store.IsBusy`
                                          Visibility=`Store.IsBusy ? Visibility.Visible : Visibility.Collapsed` VerticalAlignment=Center />
                        </StackPanel>
                    }
                </StackPanel>
                <Button Grid.Column=1 Background=`transparent` BorderThickness=0 Padding=`new Thickness(8, 2)` CornerRadius=6 VerticalAlignment=Center
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
                                    <TextBlock Grid.Column=1 Text=`Store.UsageCostLabel` FontSize=12 TextAlignment=Right VerticalAlignment=Center />
                                </Grid>
                                <TextBlock Text=`Store.SubagentCount > 0 ? "Tokens (excludes subagents)" : "Tokens"` FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.TertiaryText` />
                                <Grid ColumnSpacing=12 ColumnDefinitions=<>
                                    <ColumnDefinition Width=96 />
                                    <ColumnDefinition />
                                </>>
                                    <TextBlock Text="Input*" FontSize=12 Foreground=`theme.SecondaryText` />
                                    <TextBlock Grid.Column=1 Text=`Store.UsageTokensInput.ToString("N0")` FontSize=12 TextAlignment=Right />
                                </Grid>
                                <Grid ColumnSpacing=12 ColumnDefinitions=<>
                                    <ColumnDefinition Width=96 />
                                    <ColumnDefinition />
                                </>>
                                    <TextBlock Text="Output*" FontSize=12 Foreground=`theme.SecondaryText` />
                                    <TextBlock Grid.Column=1 Text=`Store.UsageTokensOutput.ToString("N0")` FontSize=12 TextAlignment=Right />
                                </Grid>
                                if (`Store.UsageTokensReasoning > 0`)
                                    <Grid ColumnSpacing=12 ColumnDefinitions=<>
                                        <ColumnDefinition Width=96 />
                                        <ColumnDefinition />
                                    </>>
                                        <TextBlock Text="Reasoning*" FontSize=12 Foreground=`theme.SecondaryText` />
                                        <TextBlock Grid.Column=1 Text=`Store.UsageTokensReasoning.ToString("N0")` FontSize=12 TextAlignment=Right />
                                    </Grid>
                                if (`Store.UsageTokensCacheRead > 0`)
                                    <Grid ColumnSpacing=12 ColumnDefinitions=<>
                                        <ColumnDefinition Width=96 />
                                        <ColumnDefinition />
                                    </>>
                                        <TextBlock Text="Cache read*" FontSize=12 Foreground=`theme.SecondaryText` />
                                        <TextBlock Grid.Column=1 Text=`Store.UsageTokensCacheRead.ToString("N0")` FontSize=12 TextAlignment=Right />
                                    </Grid>
                                if (`Store.UsageTokensCacheWrite > 0`)
                                    <Grid ColumnSpacing=12 ColumnDefinitions=<>
                                        <ColumnDefinition Width=96 />
                                        <ColumnDefinition />
                                    </>>
                                        <TextBlock Text="Cache write*" FontSize=12 Foreground=`theme.SecondaryText` />
                                        <TextBlock Grid.Column=1 Text=`Store.UsageTokensCacheWrite.ToString("N0")` FontSize=12 TextAlignment=Right />
                                    </Grid>
                                <Grid ColumnSpacing=12 ColumnDefinitions=<>
                                    <ColumnDefinition Width=96 />
                                    <ColumnDefinition />
                                </>>
                                    <TextBlock Text="Total" FontSize=12 FontWeight=`FontWeights.SemiBold` Foreground=`theme.SecondaryText` />
                                    <TextBlock Grid.Column=1 Text=`Store.UsageTokensLabel` FontSize=12 FontWeight=`FontWeights.SemiBold` TextAlignment=Right />
                                </Grid>
                                <TextBlock Text="*based on last message" FontSize=11 Foreground=`theme.TertiaryText` />
                                <TextBlock Text="Context" FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.TertiaryText` />
                                <Grid ColumnSpacing=12 ColumnDefinitions=<>
                                    <ColumnDefinition Width=96 />
                                    <ColumnDefinition />
                                </>>
                                    <TextBlock Text="Used" FontSize=12 Foreground=`theme.SecondaryText` />
                                    <TextBlock Grid.Column=1 Text=`Store.UsageTokensLabel` FontSize=12 TextAlignment=Right />
                                </Grid>
                                <Grid ColumnSpacing=12 ColumnDefinitions=<>
                                    <ColumnDefinition Width=96 />
                                    <ColumnDefinition />
                                </>>
                                    <TextBlock Text="Max" FontSize=12 Foreground=`theme.SecondaryText` />
                                    <TextBlock Grid.Column=1 Text=`Store.ContextLimit > 0 ? Store.ContextLimit.ToString("N0") : "--"` FontSize=12 TextAlignment=Right />
                                </Grid>
                                <ProgressBar Value=`Store.ContextUsage` Minimum=0 Maximum=100 Height=4 />
                            </StackPanel>
                        </Flyout>>
                    <StackPanel Orientation=Horizontal Spacing=8>
                        <TextBlock Text=`Store.UsageCostLabel` FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                        <TextBlock Text="·" FontSize=12 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
                        <TextBlock Text=`Store.UsageTokensLabel` FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                        <TextBlock Text="tokens" FontSize=11 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
                        <TextBlock Text="·" FontSize=12 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
                        <TextBlock Text=`Store.ContextLabel` FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                        <TextBlock Text="ctx" FontSize=11 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
                        <ProgressBar Value=`Store.ContextUsage` Minimum=0 Maximum=100 Width=70 Height=4 VerticalAlignment=Center />
                    </StackPanel>
                </Button>
            </Grid>
            <StackPanel Grid.Row=1 Padding=`new Thickness(16, 0, 16, 4)` Spacing=6>
                if (`Store.StatusMessage.Length > 0`)
                    <Border Background=`theme.SystemCautionBackground` CornerRadius=6 Padding=`new Thickness(10, 6)`
                            BorderBrush=`theme.SystemCaution` BorderThickness=`new Thickness(1)` HorizontalAlignment=Stretch>
                        <StackPanel Orientation=Horizontal Spacing=8>
                            <ProgressRing Width=14 Height=14 IsActive=true VerticalAlignment=Center />
                            <TextBlock Text=`Store.StatusMessage` FontSize=12 Foreground=`theme.SystemCaution` TextWrapping=Wrap IsTextSelectionEnabled=true VerticalAlignment=Center />
                        </StackPanel>
                    </Border>
                if (`Store.SubagentCount > 0`)
                {
                    <StackPanel Spacing=6>
                        <TextBlock Text=`$"Subagents ({Store.SubagentCount})"` FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.SecondaryText` />
                        <ScrollViewer HorizontalScrollBarVisibility=Auto VerticalScrollBarVisibility=Disabled>
                            <StackPanel Orientation=Horizontal Spacing=6>
                                foreach (var s in `Store.ActiveSubagents`; `s.Id`)
                                {
                                    <Button Padding=`new Thickness(10, 6)` CornerRadius=6 Background=`theme.CardBackground` BorderBrush=`theme.CardStroke` BorderThickness=1
                                            @Click+=`await Store.SwitchSessionAsync(s.Id)`
                                            ToolTipService.ToolTip=`s.Title`>
                                        <StackPanel Orientation=Horizontal Spacing=6>
                                            <Grid Width=14 Height=14 VerticalAlignment=Center>
                                                <AppSymbolIcon Symbol=`SubagentAttentionSymbol(s)` FontSize=10 Foreground=`theme.SystemAttention` Visibility=`s.NeedsAttention ? Visibility.Visible : Visibility.Collapsed` HorizontalAlignment=Center VerticalAlignment=Center />
                                                <ProgressRing Width=12 Height=12 IsActive=`s.IsBusy` Visibility=`!s.NeedsAttention && s.IsBusy ? Visibility.Visible : Visibility.Collapsed` HorizontalAlignment=Center VerticalAlignment=Center />
                                                <AppSymbolIcon Symbol=`SubagentOutcomeSymbol(s)` FontSize=10 Foreground=`SubagentOutcomeBrush(s)` Visibility=`!s.NeedsAttention && !s.IsBusy && s.Outcome.Length > 0 ? Visibility.Visible : Visibility.Collapsed` HorizontalAlignment=Center VerticalAlignment=Center />
                                            </Grid>
                                            <TextBlock Text=`s.Title` FontSize=12 TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                                        </StackPanel>
                                    </Button>
                                }
                            </StackPanel>
                        </ScrollViewer>
                    </StackPanel>
                }
            </StackPanel>
            <Grid Grid.Row=2>
                scrollHost = <ScrollViewer>
                    <StackPanel Padding=16>
                        if (`Store.HiddenMessages > 0`)
                            <Border Background=`theme.CardBackground` CornerRadius=6 Padding=`new Thickness(10, 8)` Margin=`new Thickness(0, 0, 0, 8)`>
                                <TextBlock Text=`$"History truncated: {Store.HiddenMessages} earlier message(s) removed for performance."` FontSize=11 Foreground=`theme.SecondaryText` TextWrapping=Wrap />
                            </Border>
                        // Keyed by message id so QuickMarkup reuses MessageView blocks across
                        // collection resets (session switches/rebuilds) instead of recreating
                        // every element; the revert filter below then only toggles visibility.
                        foreach (var m in `Store.Messages`; `m.Id`)
                        {
                            // Undo: the server keeps reverted messages until the next prompt, so
                            // hide everything at/after the revert point (the card replaces them).
                            if (`Store.RevertMessageId.Length == 0 || StringComparer.Ordinal.Compare(m.Id, Store.RevertMessageId) < 0`)
                                <MessageView Message=`m` />
                        }
                        if (`Store.RevertMessageId.Length > 0`)
                        {
                            <Border Background=`theme.CardBackground` CornerRadius=8 Padding=`new Thickness(12, 10)` Margin=`new Thickness(0, 8, 0, 0)`
                                    BorderBrush=`theme.SystemCaution` BorderThickness=`new Thickness(1)` MaxWidth=640 HorizontalAlignment=Left>
                                <StackPanel Spacing=6>
                                    <StackPanel Orientation=Horizontal Spacing=8>
                                        <AppSymbolIcon Symbol=Undo FontSize=14 Foreground=`theme.SystemCaution` VerticalAlignment=Center />
                                        <TextBlock Text=`Store.RevertCountLabel` FontSize=12 FontWeight=`FontWeights.SemiBold` VerticalAlignment=Center />
                                    </StackPanel>
                                    <StackPanel Orientation=Horizontal Spacing=8>
                                        <Button Content="Redo" @Click+=`await RedoLastMessageAsync()` CornerRadius=6 Padding=`new Thickness(10, 4)` />
                                        <TextBlock Text="Click redo to restore the reverted messages and continue from here." FontSize=11 Foreground=`theme.SecondaryText` TextWrapping=Wrap VerticalAlignment=Center />
                                    </StackPanel>
                                </StackPanel>
                            </Border>
                        }
                        if (`Store.IsRetrying`)
                            <Border Background=`theme.SystemCautionBackground` CornerRadius=8 Padding=`new Thickness(12, 10)` Margin=`new Thickness(0, 8, 0, 0)`
                                    BorderBrush=`theme.SystemCaution` BorderThickness=`new Thickness(1)` MaxWidth=640 HorizontalAlignment=Left>
                                <StackPanel Spacing=6>
                                    <StackPanel Orientation=Horizontal Spacing=8>
                                        <ProgressRing Width=14 Height=14 IsActive=true VerticalAlignment=Center />
                                        <TextBlock Text="Auto-retrying" FontSize=12 FontWeight=`FontWeights.SemiBold` VerticalAlignment=Center />
                                    </StackPanel>
                                    if (`Store.RetryMessage.Length > 0`)
                                        <TextBlock Text=`Store.RetryMessage` FontSize=12 Foreground=`theme.SecondaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
                                    <TextBlock Text=`Store.RetryCountdown` FontSize=11 Foreground=`theme.SystemCaution` TextWrapping=Wrap />
                                </StackPanel>
                            </Border>
                        if (`Store.TurnStoppedWithError`)
                            <Button Content="⟳ Continue" CornerRadius=6 HorizontalAlignment=Left Margin=`new Thickness(0, 8, 0, 0)`
                                    ToolTipService.ToolTip=`"Sends a message with content \"continue\" to resume the work from the last incomplete step."`
                                    @Click+=`await ContinueAsync()` />
                        if (`Store.ActivePermission is not null`)
                        {
                            <Border Background=`theme.CardBackground` CornerRadius=8 Padding=`new Thickness(12, 10)` Margin=`new Thickness(0, 8, 0, 0)`
                                    BorderBrush=`theme.SystemCaution` BorderThickness=`new Thickness(1)` MaxWidth=640 HorizontalAlignment=Left>
                                <StackPanel Spacing=8>
                                    <StackPanel Spacing=2>
                                        <TextBlock Text=`Store.ActivePermission?.Title ?? ""` FontSize=13 FontWeight=`FontWeights.SemiBold` TextWrapping=Wrap IsTextSelectionEnabled=true />
                                        if (`(Store.ActivePermission?.Body?.Length ?? 0) > 0`)
                                            <TextBlock Text=`Store.ActivePermission?.Body ?? ""` FontSize=11 FontFamily="Consolas" Foreground=`theme.SecondaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
                                        if (`(Store.ActivePermission?.PatternsText?.Length ?? 0) > 0`)
                                            <TextBlock Text=`Store.ActivePermission?.PatternsText ?? ""` FontSize=10 FontFamily="Consolas" Foreground=`theme.TertiaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
                                    </StackPanel>
                                    if (`PermissionStage == "reject"`)
                                        <StackPanel Spacing=8>
                                            <TextBox Text<=>`RejectText` PlaceholderText="Reason for rejection (optional)" AcceptsReturn=false MinHeight=36 />
                                            <StackPanel Orientation=Horizontal Spacing=8>
                                                <Button Content="Cancel" @Click+=`CancelPermission()` CornerRadius=6 />
                                                <Button Content="Deny" @Click+=`await RejectPermissionAsync()` CornerRadius=6 />
                                            </StackPanel>
                                        </StackPanel>
                                    else
                                        <StackPanel Orientation=Horizontal Spacing=8>
                                            <Button Content="Allow once" @Click+=`await AllowPermissionOnceAsync()` CornerRadius=6 />
                                            <Button Content="Always allow" @Click+=`await AllowPermissionAlwaysAsync()` CornerRadius=6 />
                                            <Button Content="Deny…" @Click+=`StartReject()` CornerRadius=6 />
                                        </StackPanel>
                                </StackPanel>
                            </Border>
                        }
                    </StackPanel>
                </ScrollViewer>
            </Grid>
            <ScrollViewer Grid.Row=3 MaxHeight=96 Padding=`new Thickness(16, 0, 16, 0)`
                          HorizontalScrollBarVisibility=Auto VerticalScrollBarVisibility=Disabled
                          Visibility=`Store.PendingImageCount > 0 ? Visibility.Visible : Visibility.Collapsed`>
                <StackPanel Orientation=Horizontal>
                    foreach (var a in `Store.PendingImages`)
                    {
                        <Grid Margin=`new Thickness(0, 4, 8, 4)`>
                            <Border Width=64 Height=64 CornerRadius=6 BorderBrush=`theme.CardStroke`
                                    BorderThickness=`new Thickness(1)` Background=`theme.CardBackground`
                                    VerticalAlignment=Top>
                                <Image Source=`a.Preview` Stretch=Uniform Margin=2 />
                            </Border>
                            <Button Width=18 Height=18 Padding=0 HorizontalAlignment=Right VerticalAlignment=Top
                                    CornerRadius=9 Background=`theme.CardBackground` BorderBrush=`theme.CardStroke`
                                    BorderThickness=`new Thickness(1)` Foreground=`theme.PrimaryText` FontSize=10
                                    ToolTipService.ToolTip="Remove attachment" @Click+=`Store.RemovePendingImage(a)`>
                                <TextBlock Text="✕" FontSize=10 />
                            </Button>
                        </Grid>
                    }
                </StackPanel>
            </ScrollViewer>
            <Grid Grid.Row=4 ColumnSpacing=8 Padding=`new Thickness(16, 8, 16, 16)` ColumnDefinitions=<>
                <ColumnDefinition />
                <ColumnDefinition Width=Auto />
            </>>
                suggestBox = <SuggestBox Text<=>`Input` PlaceholderText="Message OpenCode..." IsEnabled=`Store.ActivePermission is null`
                    PreviewKeyDown+=`OnPreviewKeyDown` SubmitRequested+=`OnSubmitRequested` />
                <StackPanel Grid.Column=1 Orientation=Horizontal Spacing=8 VerticalAlignment=Bottom>
                    <Button ToolTipService.ToolTip="Attach image" CornerRadius=6 IsEnabled=`Store.ActivePermission is null`
                            @Click+=`await Store.PickImageAsync()`>
                        <SymbolIcon Symbol=Camera VerticalAlignment=Center />
                    </Button>
                    <Button ToolTipService.ToolTip="Undo last message" CornerRadius=6 IsEnabled=`Store.ActivePermission is null`
                            @Click+=`await UndoLastMessageAsync()`>
                        <SymbolIcon Symbol=Undo VerticalAlignment=Center />
                    </Button>
                    if (`Store.PendingPrompts > 0`)
                        <Border Background=`theme.SystemCautionBackground` CornerRadius=6 Padding=`new Thickness(8, 4, 8, 4)` VerticalAlignment=Center>
                            <TextBlock Text=`$"⏳ {Store.PendingPrompts} queued"` FontSize=11 Foreground=`theme.SystemCaution` VerticalAlignment=Center />
                        </Border>
                    if (`Store.IsBusy`)
                        <Button Content="⏹ Stop" @Click+=`await Store.InterruptAsync()` CornerRadius=6 />
                    <Button @Click+=`await SendAsync()` IsEnabled=`Store.ActivePermission is null`>
                        <SymbolIcon Symbol=Send VerticalAlignment=Center />
                    </Button>
                </StackPanel>
            </Grid>
            <StackPanel Grid.Row=5 Orientation=Horizontal Spacing=12 Padding=`new Thickness(16, 0, 16, 10)`>
                <StackPanel Orientation=Horizontal Spacing=6 VerticalAlignment=Center>
                    <TextBlock Text="Mode" FontSize=10 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                    modeCombo = <ComboBox ItemsSource=`Store.ModeOptions` SelectedItem=`Store.Mode` ItemTemplate=template (string? value) { <TextBlock Text=`Capitalize(value)` /> } SelectionChanged+=`(sender, e) => OnModeChanged(sender, e)` MinWidth=90 Height=28 FontSize=12 />
                </StackPanel>
                <StackPanel Orientation=Horizontal Spacing=6 VerticalAlignment=Center>
                    <TextBlock Text="Model" FontSize=10 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                    modelCombo = <ComboBox ItemsSource=`Store.ModelOptions` DisplayMemberPath="Name" SelectedValuePath="Id" SelectedValue=`Store.ModelId` SelectionChanged+=`(sender, e) => OnModelChanged(sender, e)` MinWidth=200 MaxWidth=300 Height=28 FontSize=12 />
                </StackPanel>
                <StackPanel Orientation=Horizontal Spacing=6 VerticalAlignment=Center>
                    <TextBlock Text="Variant" FontSize=10 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                    variantCombo = <ComboBox ItemsSource=`Store.VariantOptions` SelectedItem=`Store.Variant` IsEnabled=`Store.HasVariants` ItemTemplate=template (string? value) { <TextBlock Text=`Capitalize(value)` /> } SelectionChanged+=`(sender, e) => OnVariantChanged(sender, e)` MinWidth=90 Height=28 FontSize=12 />
                </StackPanel>
            </StackPanel>
        </Grid>
    </root>
    """)]
public partial class ChatPage : Page
{
    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();

        var store = Store;
        store.Messages.CollectionChanged += OnMessagesChanged;
        foreach (var message in store.Messages) HookParts(message);

        store.ActivePermissionProp.Watch(_newReq =>
        {
            PermissionStage = "choose";
            RejectText = "";
            _ = ScrollToPermissionAsync();
        });

        // One-second tick that keeps the end-of-chat retry card's countdown live.
        var countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        countdown.Tick += (_, _) => store.UpdateRetryCountdown();
        countdown.Start();

        _ = store.ConnectAsync();

        // Suggestion sources for the input box. Server-backed providers (commands, skills, files)
        // return empty lists when the server is unreachable or has no data (no mock fallback — the
        // box simply shows nothing); the directory is read fresh on every query so it tracks the
        // active session.
        suggestBox.Providers = new ISuggestionProvider[]
        {
            new ServerCommandSuggestionProvider(() => Store.Client, Store.ActiveDirectory),
            new ServerSkillSuggestionProvider(() => Store.Client, Store.ActiveDirectory),
            new ServerFileSuggestionProvider(() => Store.Client, Store.ActiveDirectory),
        };

        suggestBox.MarkupNode.Focus(FocusState.Programmatic);
    }

    /// <summary>Scrolls to the permission card once it has been added to the message list.</summary>
    private async Task ScrollToPermissionAsync()
    {
        await Task.Yield();
        ScrollToBottom();
    }

    private async void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.V &&
            InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) &&
            !InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            if (await Store.PasteImageFromClipboardAsync())
                e.Handled = true;
        }
    }

    private async Task SendAsync(string? text = null)
    {
        var content = (text ?? Input).Trim();
        if (content.Length == 0 && Store.PendingImages.Count == 0) return;
        Input = "";
        await Store.SendAsync(content);
        ScrollToBottom();
    }

    /// <summary>Enter was pressed in the input box with the suggestion flyout closed — send the message.</summary>
    private async Task OnSubmitRequested(SuggestBox sender, string text)
    {
        await SendAsync(text);
        sender.Clear();
    }

    /// <summary>
    /// Resumes a turn that stopped with an error. Sends a "continue" user message — the agent
    /// is instructed (prompt/beast.txt) to pick up from the last incomplete step in its todo
    /// list. Matches the TUI, which has no separate continue API: it's just a user message.
    /// </summary>
    private async Task ContinueAsync()
    {
        await Store.SendAsync("continue");
        ScrollToBottom();
    }

    /// <summary>
    /// Undo the agent's reply to the last user message (revert), then restore the undone
    /// prompt into the composer so it can be resent (mirrors the TUI's /undo).
    /// </summary>
    private async Task UndoLastMessageAsync()
    {
        await Store.UndoLastMessageAsync();
        Input = Store.RevertPromptText;
        ScrollToBottom();
    }

    /// <summary>Restore reverted messages (redo the undo), then scroll to the end.</summary>
    private async Task RedoLastMessageAsync()
    {
        await Store.RedoLastMessageAsync();
        ScrollToBottom();
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (MessageItem message in e.NewItems) HookParts(message);
        ScrollToBottom();
    }

    private void HookParts(MessageItem message) =>
        message.Parts.CollectionChanged += (_, _) => ScrollToBottom();

    private void OnModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if ((sender as ComboBox)?.SelectedItem is string mode) Store.SetMode(mode);
    }

    private void OnModelChanged(object? sender, SelectionChangedEventArgs e)
    {
        if ((sender as ComboBox)?.SelectedValue is string id) Store.SetModel(id);
    }

    private void OnVariantChanged(object? sender, SelectionChangedEventArgs e)
    {
        if ((sender as ComboBox)?.SelectedItem is string variant) Store.SetVariant(variant);
    }

    private static string Capitalize(string? value) =>
        string.IsNullOrEmpty(value) ? "" : char.ToUpper(value[0]) + value.Substring(1);

    private void StartTitleEdit()
    {
        TitleEdit = Store.SessionTitle;
        EditingTitle = true;
        _ = FocusTitleEditAsync();
    }

    private void CancelTitleEdit() => EditingTitle = false;

    private async Task SaveTitleAsync()
    {
        EditingTitle = false;
        await Store.RenameSessionAsync(TitleEdit);
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

    private async Task AllowPermissionOnceAsync()
    {
        var req = Store.ActivePermission;
        if (req is null) return;
        await Store.ReplyPermissionAsync(req.Id, "once");
    }

    private async Task AllowPermissionAlwaysAsync()
    {
        var req = Store.ActivePermission;
        if (req is null) return;
        await Store.ReplyPermissionAsync(req.Id, "always");
    }

    private void StartReject() => PermissionStage = "reject";

    private async Task RejectPermissionAsync()
    {
        var req = Store.ActivePermission;
        if (req is null) return;
        await Store.ReplyPermissionAsync(req.Id, "reject", RejectText.Trim());
    }

    private void CancelPermission() => PermissionStage = "choose";

    /// <summary>Icon for a subagent chip's turn outcome: check = success, X = error, stop = interrupted.</summary>
    private static Symbol SubagentOutcomeSymbol(SessionInfo s) => s.Outcome switch
    {
        "error" => Symbol.Cancel,
        "interrupted" => Symbol.Stop,
        _ => Symbol.Accept,
    };

    /// <summary>Color for <see cref="SubagentOutcomeSymbol"/>: green success, red error, caution interrupted.</summary>
    private static Brush? SubagentOutcomeBrush(SessionInfo s) => s.Outcome switch
    {
        "error" => ThemeBrushes.Global.SystemCritical,
        "interrupted" => ThemeBrushes.Global.SystemCaution,
        _ => ThemeBrushes.Global.SystemSuccess,
    };

    /// <summary>Glyph for a pending question/approval on a subagent chip: shield for a permission, question mark for a question.</summary>
    private static Symbol SubagentAttentionSymbol(SessionInfo s) => s.AttentionKind == "permission" ? Symbol.Permissions : Symbol.Help;

    private void ScrollToBottom()
    {
        if (scrollHost is null) return;
        scrollHost.ChangeView(null, scrollHost.ScrollableHeight, null);
    }
}
