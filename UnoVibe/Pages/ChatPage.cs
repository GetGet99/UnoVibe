using System.Collections.Specialized;
using Microsoft.UI.Input;
using UnoVibe.Models;
using UnoVibe.Services;

namespace UnoVibe.Pages;

[QuickMarkup("""
    using UnoVibe.Models;
    using UnoVibe.Services;
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    using Microsoft.UI;
    using Microsoft.UI.Xaml.Controls.Primitives;
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
                                <TextBlock Text="Tokens" FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.TertiaryText` />
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
            <Grid Grid.Row=1 Padding=`new Thickness(16, 0, 16, 4)`>
                if (`Store.StatusMessage.Length > 0`)
                    <Border Background=`theme.SystemCautionBackground` CornerRadius=6 Padding=`new Thickness(10, 6)`
                            BorderBrush=`theme.SystemCaution` BorderThickness=`new Thickness(1)` HorizontalAlignment=Stretch>
                        <StackPanel Orientation=Horizontal Spacing=8>
                            <ProgressRing Width=14 Height=14 IsActive=true VerticalAlignment=Center />
                            <TextBlock Text=`Store.StatusMessage` FontSize=12 Foreground=`theme.SystemCaution` TextWrapping=Wrap IsTextSelectionEnabled=true VerticalAlignment=Center />
                        </StackPanel>
                    </Border>
            </Grid>
            <Grid Grid.Row=2>
                scrollHost = <ScrollViewer>
                    <StackPanel Padding=16>
                        if (`Store.HiddenMessages > 0`)
                            <Border Background=`theme.CardBackground` CornerRadius=6 Padding=`new Thickness(10, 8)` Margin=`new Thickness(0, 0, 0, 8)`>
                                <TextBlock Text=`$"History truncated: {Store.HiddenMessages} earlier message(s) removed for performance."` FontSize=11 Foreground=`theme.SecondaryText` TextWrapping=Wrap />
                            </Border>
                        foreach (var m in `Store.Messages`)
                            <MessageView Message=`m` />
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
            <Grid Grid.Row=3 ColumnSpacing=8 Padding=`new Thickness(16, 8, 16, 16)` ColumnDefinitions=<>
                <ColumnDefinition />
                <ColumnDefinition Width=Auto />
            </>>
                inputBox = <TextBox Text<=>`Input` PlaceholderText="Message OpenCode..." AcceptsReturn=true TextWrapping=Wrap MinHeight=36 MaxHeight=120 IsEnabled=`Store.ActivePermission is null` PreviewKeyDown+=`OnPreviewKeyDown` />
                <StackPanel Grid.Column=1 Orientation=Horizontal Spacing=8 VerticalAlignment=Bottom>
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
            <Grid Grid.Row=4 ColumnSpacing=12 Padding=`new Thickness(16, 0, 16, 10)` ColumnDefinitions=<>
                <ColumnDefinition Width=Auto />
                <ColumnDefinition Width=Auto />
                <ColumnDefinition Width=Auto />
            </>>
                <StackPanel Spacing=4>
                    <TextBlock Text="Mode" FontSize=10 Foreground=`theme.SecondaryText` />
                    modeCombo = <ComboBox ItemsSource=`Store.ModeOptions` SelectedItem=`Store.Mode` ItemTemplate=template (string? value) { <TextBlock Text=`Capitalize(value)` /> } SelectionChanged+=`(sender, e) => OnModeChanged(sender, e)` MinWidth=90 />
                </StackPanel>
                <StackPanel Grid.Column=1 Spacing=4>
                    <TextBlock Text="Model" FontSize=10 Foreground=`theme.SecondaryText` />
                    modelCombo = <ComboBox ItemsSource=`Store.ModelOptions` DisplayMemberPath="Name" SelectedValuePath="Id" SelectedValue=`Store.ModelId` SelectionChanged+=`(sender, e) => OnModelChanged(sender, e)` MinWidth=200 MaxWidth=300 />
                </StackPanel>
                <StackPanel Grid.Column=2 Spacing=4>
                    <TextBlock Text="Variant" FontSize=10 Foreground=`theme.SecondaryText` />
                    variantCombo = <ComboBox ItemsSource=`Store.VariantOptions` SelectedItem=`Store.Variant` IsEnabled=`Store.HasVariants` ItemTemplate=template (string? value) { <TextBlock Text=`Capitalize(value)` /> } SelectionChanged+=`(sender, e) => OnVariantChanged(sender, e)` MinWidth=90 />
                </StackPanel>
            </Grid>
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

        _ = store.ConnectAsync();
        inputBox.Focus(FocusState.Programmatic);
    }

    /// <summary>Scrolls to the permission card once it has been added to the message list.</summary>
    private async Task ScrollToPermissionAsync()
    {
        await Task.Yield();
        ScrollToBottom();
    }

    private async void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        if (InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            return;
        e.Handled = true;
        _ = SendAsync();
        inputBox.Text = "";
        inputBox.AcceptsReturn = false;
        await Task.Delay(16);
        inputBox.AcceptsReturn = true;
    }

    private async Task SendAsync()
    {
        var text = Input.Trim();
        if (text.Length == 0) return;
        Input = "";
        await Store.SendAsync(text);
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

    private void ScrollToBottom()
    {
        if (scrollHost is null) return;
        scrollHost.ChangeView(null, scrollHost.ScrollableHeight, null);
    }
}
