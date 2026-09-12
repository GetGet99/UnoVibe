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
/// <see cref="SendShellCommandAsync"/> for shell-mode submits ("!" prefix).
/// </summary>
[QuickMarkup("""
    using UnoVibe.Services;
    using UnoVibe.Models;
    using UnoVibe.Providers;
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Microsoft.UI;
    inject Window HostWindow;
    inject? bool IsCompact;
    inject ChatPage ChatP;
    inject bool SettingsOpen;
    inject SessionId? ActiveSessionId;
    inject SessionsSource Sessions;
    inject `UnoVibe.Integration.OpencodeClient` Opencode;
    inject ToastService Toasts;
    inject UIService UIs;
    inject ModelsProvider Models;
    string SendMode = "";
    // Shell mode (TUI parity): "!" typed as the entire input flips the composer into shell
    // command entry; Esc, the ✕ button, or submitting leaves it again. Submit runs
    // POST /session/{id}/shell instead of a prompt.
    bool ShellMode = false;
    `IReadOnlyList<string>` SelectionVarients => `
        Sessions.ActiveChatParams.Model is not {} model
        ? EmptyList
        : Models.ModelOptions[Sessions.ActiveChatParams.Model].Varients`;
    bool IsBusy => `Sessions.ActiveHead?.IsBusy ?? false`;
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
                    if (`IsBusy`)
                        <Button Content="⏹ Stop" @Click+=`await Store.Active.InterruptAsync()` CornerRadius=6 />
                    <SendMessageButton Mode=`SendMode` IsBusy=`IsBusy` Enabled=`StoreToUpdate.ActivePermission is null`
                                       SendRequested+=`OnSendWithMode` />
                </StackPanel>
            </Grid>
            if (`!ShellMode`)
                <StackPanel Grid.Row=2 Orientation=Horizontal Spacing=`IsCompact ? 8 : 12` Padding=`new Thickness(IsCompact ? 12 : 16, 0, IsCompact ? 12 : 16, 10)`>
                    <StackPanel Orientation=Horizontal Spacing=6 VerticalAlignment=Center>
                        <TextBlock Text="Mode" FontSize=10 Foreground=`theme.SecondaryText` VerticalAlignment=Center Visibility=`IsCompact ? Visibility.Collapsed : Visibility.Visible` />
                        modeCombo = <ComboBox
                            ItemsSource=`Models.AgentOptions`
                            SelectedItem=`Sessions.ActiveChatParams.Agent`
                            ItemTemplate=template (string? value) { <TextBlock Text=`Capitalize(value) ?? "Build"` /> }
                            SelectedItem+=>`x => Sessions.ActiveChatParams.Agent = x as string`
                            MinWidth=`IsCompact ? 76 : 90`
                            Height=28
                            FontSize=12
                        />
                    </StackPanel>
                    <StackPanel Orientation=Horizontal Spacing=6 VerticalAlignment=Center>
                        <TextBlock Text="Model" FontSize=10 Foreground=`theme.SecondaryText` VerticalAlignment=Center Visibility=`IsCompact ? Visibility.Collapsed : Visibility.Visible` />
                        modelPicker = <ModelPicker />
                    </StackPanel>
                    <StackPanel Orientation=Horizontal Spacing=6 VerticalAlignment=Center>
                        <TextBlock Text="Variant" FontSize=10 Foreground=`theme.SecondaryText` VerticalAlignment=Center Visibility=`IsCompact ? Visibility.Collapsed : Visibility.Visible` />
                        variantCombo = <ComboBox
                            ItemsSource=`SelectionVarients`
                            SelectedItem=`Sessions.ActiveChatParams.Variant`
                            IsEnabled=`SelectionVarients.Count > 0`
                            ItemTemplate=template (string? value) { <TextBlock Text=`Capitalize(value) ?? "Default"` /> }
                            SelectedItem+=>`x => Sessions.ActiveChatParams.Variant = x as string`
                            MinWidth=`IsCompact ? 76 : 90`
                            Height=28
                            FontSize=12
                        />
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
    static readonly IReadOnlyList<string> EmptyList = [];
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

        // Suggestion sources for the input box. Built-in commands are local (the availability
        // predicate hides context-dependent ones like /interrupt while nothing is running);
        // server-backed providers (commands, skills, files) return empty lists when the server is
        // unreachable or has no data (no mock fallback — the box simply shows nothing); the
        // directory is read fresh on every query so it tracks the active session.
        suggestBox.Providers =
        [
            new BuiltInCommandSuggestionProvider(IsBuiltInAvailable),
            new ServerCommandSuggestionProvider(() => Opencode, () => Sessions.ActiveSessionDirectory),
            new ServerSkillSuggestionProvider(() => Opencode, () => Sessions.ActiveSessionDirectory),
            new ServerFileSuggestionProvider(() => Opencode, () => Sessions.ActiveSessionDirectory),
        ];
        suggestBox.CommandTriggered += (sender, item) => RunBuiltInCommandAsync(item.Action!);

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
        if (command.Length == 0) return;
        await SendShellCommandAsync(command);
    }

    /// <summary>Enter was pressed in the input box with the suggestion flyout closed — run a built-in
    /// command when the text is one (e.g. "/new" typed with the flyout dismissed), else send the
    /// message (or run the shell command when shell mode is active).</summary>
    private async Task OnSubmitRequested(SuggestBox sender, string text)
    {
        if (ShellMode)
        {
            await SubmitShellAsync();
            return;
        }
        if (await TryRunBuiltInTextAsync(text)) return;
        await SendAsync(text, null);
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
        if (await TryRunBuiltInTextAsync(suggestBox.MarkupNode.Text)) return;
        await SendAsync(suggestBox.MarkupNode.Text, mode);
        suggestBox.Clear();
    }

    // ── Built-in slash commands (/new /models /agents /variants /connect /editor /explorer
    //    /terminal /mcps /fork /rename /setting /interrupt /continue /undo /redo) ──

    /// <summary>
    /// Availability predicate for the built-in command flyout: context-dependent rows are hidden
    /// when they make no sense right now (e.g. <c>/interrupt</c> only while the session runs).
    /// </summary>
    private bool IsBuiltInAvailable(string name) => name != "interrupt" || IsBusy;

    /// <summary>
    /// Runs the action for a built-in command row committed from the suggestion flyout
    /// (Tab / Enter / mouse click — the box has already cleared its input).
    /// </summary>
    private async Task RunBuiltInCommandAsync(string name)
    {
        switch (name)
        {
            case "agents":
                OpenCombo(modeCombo);
                break;
            case "connect":
                await ProviderConnectDialog.ShowAsync(Opencode, Toasts, MarkupNode.XamlRoot!);
                break;
            case "continue":
                // Same as the ⟳ Continue card: a literal "continue" user message the agent is
                // instructed to treat as "pick up where you stopped".
                await SendAsync("continue", null);
                break;
            case "editor":
                LaunchFolder(FolderLauncher.OpenInEditor);
                break;
            case "explorer":
                LaunchFolder(FolderLauncher.OpenInFileManager);
                break;
            case "fork":
                if (Sessions.ActiveSessionId is {} sessionId)
                    UIs.ForkAndSwitchSession(sessionId);
                else
                    Toasts.ShowWarning("No session to fork.", "/fork");
                break;
            case "interrupt":
                if (!IsBusy)
                    Toasts.ShowWarning("Nothing is running right now.", "/interrupt");
                else
                    await Store.Active.InterruptAsync();
                break;
            case "mcps":
                UIs.InvokeMcpSectionRequested();
                break;
            case "models":
                modelPicker.Open();
                break;
            case "new":
                Sessions.PrepareNewSession(Sessions.ActiveSessionDirectory);
                break;
            case "redo":
                await ChatP.RedoLastAsync();
                break;
            case "rename":
                if (ActiveSessionId is null)
                    Toasts.ShowWarning("There is no conversation to rename yet.", "/rename");
                else
                    UIs.BeginRenameAndFocus();
                break;
            case "setting":
                SettingsOpen = true;
                break;
            case "terminal":
                LaunchFolder(FolderLauncher.OpenInTerminal);
                break;
            case "undo":
                await ChatP.UndoLastAsync();
                break;
            case "variants":
                if (SelectionVarients.Count is 0)
                    Toasts.ShowWarning("The selected model has no reasoning variants.", "No variants");
                else
                    OpenCombo(variantCombo);
                break;
        }
    }

    /// <summary>Intercepts submitted text that is an exact built-in command ("/name", optional
    /// ignored arguments) so it executes instead of reaching the model verbatim. Returns true when
    /// the text was consumed.</summary>
    private async Task<bool> TryRunBuiltInTextAsync(string? text)
    {
        if (!BuiltInCommands.TryParse(text, out var command)) return false;
        await RunBuiltInCommandAsync(command.Name);
        suggestBox.Clear();
        return true;
    }

    private static void OpenCombo(ComboBox? combo)
    {
        if (combo is not null) combo.IsDropDownOpen = true;
    }

    /// <summary>The /editor //explorer //terminal built-ins: run a <see cref="FolderLauncher"/>
    /// open on the active directory, toast on failure.</summary>
    private void LaunchFolder(Func<string, string?> open)
    {
        var error = open(Sessions.ActiveSessionDirectory);
        if (error is null) return;
        Toasts.Show(new ToastItem
        {
            Title = "Open folder",
            Message = error,
            Variant = "error",
        });
    }

    private static string? Capitalize(string? value) =>
        string.IsNullOrEmpty(value) ? null : char.ToUpper(value[0]) + value[1..];

    public void SetChatText(string txt)
    {
        // A restored prompt must never land in shell mode and run as a command.
        if (ShellMode) ExitShellMode();
        suggestBox.MarkupNode.Text = txt;
    }
}
