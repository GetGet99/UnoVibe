using Microsoft.UI.Input;
using UnoVibe.Models;
using UnoVibe.Services;
using UnoVibe.Controls;

namespace UnoVibe.Pages.Chat;

/// <summary>
/// Chat page composer block: the staged-image strip, the message input (SuggestBox) with
/// attach/stop/send buttons, and the mode / model / variant pickers row. The busy-state
/// send mode sync from <see cref="SettingsStore"/>, and the suggestion providers.
/// Raises <see cref="SendRequested"/> for the page to run the send + autoscroll, and
/// <see cref="ShellCommandRequested"/> for shell-mode submits ("!" prefix).
/// </summary>
[QuickMarkup("""
    using UnoVibe.Services;
    using UnoVibe.Models;
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Microsoft.UI;
    inject ChatStore Store;
    inject Window HostWindow;
    inject? bool IsCompact;
    string SendMode = "";
    // Shell mode (TUI parity): "!" typed as the entire input flips the composer into shell
    // command entry; Esc, the ✕ button, or submitting leaves it again. Submit runs
    // POST /session/{id}/shell instead of a prompt.
    bool ShellMode = false;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        <Grid RowDefinitions=<>
            <RowDefinition Height=Auto />
            <RowDefinition Height=Auto />
            <RowDefinition Height=Auto />
        </>>
            <ScrollViewer Grid.Row=0 MaxHeight=96 Padding=`new Thickness(16, 0, 16, 0)`
                          HorizontalScrollBarVisibility=Auto VerticalScrollBarVisibility=Disabled
                          Visibility=`Store.Active.PendingImageCount > 0 ? Visibility.Visible : Visibility.Collapsed`>
                <StackPanel Orientation=Horizontal>
                    foreach (var a in `Store.Active.PendingImages`)
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
                                    ToolTipService.ToolTip="Remove attachment" @Click+=`Store.Active.RemovePendingImage(a)`>
                                <TextBlock Text="✕" FontSize=10 />
                            </Button>
                        </Grid>
                    }
                </StackPanel>
            </ScrollViewer>
            <Grid Grid.Row=1 ColumnSpacing=`IsCompact ? 6 : 8` Padding=`new Thickness(IsCompact ? 12 : 16, 8, IsCompact ? 12 : 16, IsCompact ? 12 : 16)` ColumnDefinitions=<>
                <ColumnDefinition />
                <ColumnDefinition Width=Auto />
            </>>
                suggestBox = <SuggestBox PlaceholderText=`ShellMode ? "Run a shell command… (e.g. git status)" : "Message OpenCode..."` IsEnabled=`Store.ActivePermission is null`
                    TextChanged+=`OnInputTextChanged` PreviewKeyDown+=`OnPreviewKeyDown` SubmitRequested+=`OnSubmitRequested` />
                <StackPanel Grid.Column=1 Orientation=Horizontal Spacing=8 VerticalAlignment=Bottom>
                    if (`!ShellMode`)
                        <Button ToolTipService.ToolTip="Attach image" CornerRadius=6 IsEnabled=`Store.ActivePermission is null`
                                @Click+=`await Store.Active.PickImageAsync(HostWindow)`>
                            <SymbolIcon Symbol=Camera VerticalAlignment=Center />
                        </Button>
                    if (`Store.Active.PendingPrompts > 0`)
                        <Border Background=`theme.SystemCautionBackground` CornerRadius=6 Padding=`new Thickness(8, 4, 8, 4)` VerticalAlignment=Center>
                            <TextBlock Text=`$"⏳ {Store.Active.PendingPrompts} queued"` FontSize=11 Foreground=`theme.SystemCaution` VerticalAlignment=Center />
                        </Border>
                    if (`Store.Active.IsBusy`)
                        <Button Content="⏹ Stop" @Click+=`await Store.Active.InterruptAsync()` CornerRadius=6 />
                    <SendMessageButton Mode=`SendMode` IsBusy=`Store.Active.IsBusy` Enabled=`Store.ActivePermission is null`
                                       SendRequested+=`OnSendWithMode` />
                </StackPanel>
            </Grid>
            if (`!ShellMode`)
                <StackPanel Grid.Row=2 Orientation=Horizontal Spacing=`IsCompact ? 8 : 12` Padding=`new Thickness(IsCompact ? 12 : 16, 0, IsCompact ? 12 : 16, 10)`>
                    <StackPanel Orientation=Horizontal Spacing=6 VerticalAlignment=Center>
                        <TextBlock Text="Mode" FontSize=10 Foreground=`theme.SecondaryText` VerticalAlignment=Center Visibility=`IsCompact ? Visibility.Collapsed : Visibility.Visible` />
                        modeCombo = <ComboBox ItemsSource=`Store.ModeOptions` SelectedItem=`Store.Active.Mode` ItemTemplate=template (string? value) { <TextBlock Text=`Capitalize(value)` /> } SelectionChanged+=`(sender, e) => OnModeChanged(sender, e)` MinWidth=`IsCompact ? 76 : 90` Height=28 FontSize=12 />
                    </StackPanel>
                    <StackPanel Orientation=Horizontal Spacing=6 VerticalAlignment=Center>
                        <TextBlock Text="Model" FontSize=10 Foreground=`theme.SecondaryText` VerticalAlignment=Center Visibility=`IsCompact ? Visibility.Collapsed : Visibility.Visible` />
                        <ModelPicker ItemsSource=`Store.ModelOptions` SelectedItem=`Store.Active.SelectedModelOption`
                                    ModelSelected+=`OnModelSelected` />
                    </StackPanel>
                    <StackPanel Orientation=Horizontal Spacing=6 VerticalAlignment=Center>
                        <TextBlock Text="Variant" FontSize=10 Foreground=`theme.SecondaryText` VerticalAlignment=Center Visibility=`IsCompact ? Visibility.Collapsed : Visibility.Visible` />
                        variantCombo = <ComboBox ItemsSource=`Store.VariantOptions` SelectedItem=`Store.Active.Variant` IsEnabled=`Store.Active.HasVariants` ItemTemplate=template (string? value) { <TextBlock Text=`Capitalize(value)` /> } SelectionChanged+=`(sender, e) => OnVariantChanged(sender, e)` MinWidth=`IsCompact ? 76 : 90` Height=28 FontSize=12 />
                    </StackPanel>
                </StackPanel>
            else
                <StackPanel Grid.Row=2 Orientation=Horizontal Spacing=`IsCompact ? 8 : 12` Padding=`new Thickness(IsCompact ? 12 : 16, 0, IsCompact ? 12 : 16, 10)`>
                    <TextBlock Text="! Shell mode"
                            FontSize=11 Foreground=`theme.SecondaryText` VerticalAlignment=Center TextTrimming=`TextTrimming.CharacterEllipsis` />
                    <Button Content="Exit shell mode (Esc)" CornerRadius=6 Padding=`new Thickness(10, 3, 10, 3)` @Click+=`ExitShellMode()` />
                </StackPanel>
        </Grid>
    </root>
    """)]
public partial class ChatComposer : IQuickMarkupComponent<Grid>
{
    /// <summary>Handler for <see cref="SendRequested"/>.</summary>
    public delegate Task SendRequestedHandler(string text, SendPromptMode? mode);

    /// <summary>Raised when the user triggers a send; the page runs the send and re-pins the autoscroll.</summary>
    public event SendRequestedHandler? SendRequested;

    /// <summary>Handler for <see cref="ShellCommandRequested"/>.</summary>
    public delegate Task ShellCommandHandler(string command);

    /// <summary>Raised when the user submits a shell-mode command; the page runs it in the session.</summary>
    public event ShellCommandHandler? ShellCommandRequested;

    /// <summary>UI-thread dispatcher for bouncing <see cref="SettingsStore.Changed"/> onto the UI thread.</summary>
    private DispatcherQueue? _dispatcher;

    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();

        // The busy-state send button's primary action (and menu checkmark) track the configured
        // send default live, so a change from the Settings page applies immediately. The event may
        // fire on a background thread (cross-process file watcher), so bounce to the UI thread.
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        SendMode = SettingsStore.SendMode.ToString();
        SettingsStore.Changed += OnSettingsChanged;

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

    private void OnSettingsChanged()
    {
        _ = _dispatcher?.TryEnqueue(() => SendMode = SettingsStore.SendMode.ToString());
    }

    private async void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ShellMode && e.Key == Windows.System.VirtualKey.Escape && !e.Handled)
        {
            e.Handled = true;
            ExitShellMode();
            return;
        }

        if (e.Key == Windows.System.VirtualKey.V &&
            InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) &&
            !InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            if (await Store.Active.PasteImageFromClipboardAsync())
                e.Handled = true;
        }
    }

    /// <summary>
    /// Enters shell mode when "!" lands as the entire input — the TUI's first-character rule,
    /// watched via text instead of keys so it works on any keyboard layout (and for a lone "!"
    /// paste). Emptying the input does not exit shell mode; Esc, the ✕ button, or submitting
    /// does.
    /// </summary>
    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        // EnterShellMode strips the trigger character and Uno may deliver that clear's
        // TextChanged asynchronously (after the suppress window would have closed), so this is
        // deliberately guarded by !ShellMode rather than a suppression flag: re-entry here with
        // an empty text is a no-op once shell mode is on.
        if (!ShellMode && (suggestBox.MarkupNode.Text ?? "") == "!")
            EnterShellMode();
    }

    /// <summary>Flips the composer into shell command entry and strips the "!" trigger character
    /// (TUI parity: the key never enters the buffer). "/" and "@" suggestions are disabled while
    /// shell mode is active — slash tokens in command text must not pop the flyout.</summary>
    private void EnterShellMode()
    {
        ShellMode = true;
        suggestBox.Clear();
        SetSuggestionPrefixes("");
        suggestBox.MarkupNode.Focus(FocusState.Programmatic);
    }

    /// <summary>Leaves shell mode, discarding the typed command text.</summary>
    private void ExitShellMode()
    {
        if (!ShellMode) return;
        ShellMode = false;
        suggestBox.Clear();
        SetSuggestionPrefixes("/@");
        suggestBox.MarkupNode.Focus(FocusState.Programmatic);
    }

    /// <summary>Sets the suggestion trigger prefixes. The controller caches them at construction,
    /// so Providers is re-set to force a rebuild with the new prefixes.</summary>
    private void SetSuggestionPrefixes(string prefixes)
    {
        var providers = suggestBox.Providers;
        suggestBox.Prefixes = prefixes;
        suggestBox.Providers = providers;
    }

    /// <summary>
    /// Runs the typed shell command in the session, then resets the composer (TUI parity:
    /// submitting ends shell mode).
    /// </summary>
    private async Task SubmitShellAsync()
    {
        var command = (suggestBox.MarkupNode.Text ?? "").Trim();
        ExitShellMode();
        if (command.Length == 0 || ShellCommandRequested is null) return;
        await ShellCommandRequested(command);
    }

    /// <summary>Enter was pressed in the input box with the suggestion flyout closed — send the message
    /// (or run the shell command when shell mode is active).</summary>
    private async Task OnSubmitRequested(SuggestBox sender, string text)
    {
        if (ShellMode)
        {
            await SubmitShellAsync();
            return;
        }
        if (SendRequested is not null) await SendRequested(text, null);
        sender.Clear();
    }

    /// <summary>Sends with an explicit mode (the busy-state split button's primary action or a one-shot dropdown override);
    /// in shell mode the send button runs the command instead.</summary>
    private async Task OnSendWithMode(SendPromptMode mode)
    {
        if (ShellMode)
        {
            await SubmitShellAsync();
            return;
        }
        if (SendRequested is not null) await SendRequested(suggestBox.MarkupNode.Text, mode);
        suggestBox.Clear();
    }

    private void OnModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if ((sender as ComboBox)?.SelectedItem is string mode) Store.Active.SetMode(mode);
    }

    private void OnModelSelected(ModelOption model) => Store.Active.SetModel(model.Id);

    private void OnVariantChanged(object? sender, SelectionChangedEventArgs e)
    {
        if ((sender as ComboBox)?.SelectedItem is string variant) Store.Active.SetVariant(variant);
    }

    private static string Capitalize(string? value) =>
        string.IsNullOrEmpty(value) ? "" : char.ToUpper(value[0]) + value.Substring(1);

    public void SetChatText(string txt)
    {
        // A restored prompt must never land in shell mode and run as a command.
        if (ShellMode) ExitShellMode();
        suggestBox.MarkupNode.Text = txt;
    }
}
