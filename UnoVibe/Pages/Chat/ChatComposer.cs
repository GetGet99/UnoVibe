using Microsoft.UI.Input;
using UnoVibe.Controls;
using Uno.Extensions;
namespace UnoVibe.Pages.Chat;

[QuickMarkup("""
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Microsoft.UI;
    inject Window HostWindow;
    inject bool IsCompact;
    inject ChatPage ChatP;
    inject bool SettingsOpen;
    inject SessionsStateProvider Sessions;
    inject `UnoVibe.Integration.OpencodeClient` Opencode;
    inject ToastsProvider Toasts;
    inject UIServiceProvider UIs;
    inject ModelsProvider Models;
    inject `AsyncComputed<ChatMessagesState>` ChatStateAsync;
    string SendMode = "";
    bool ShellMode = false;
    `IReadOnlyList<string>` SelectionVariants => `
        Sessions.ActiveChatParams.Model is not {} model
        ? EmptyList
        :  (Models.ModelOptions.TryGetValue(model, out var modelOption)
            ? modelOption.Variants
            : EmptyList
        )`;
    bool IsBusy => `Sessions.ActiveHead?.IsBusy ?? false`;
    private bool IsEnabled = true;
    ChatboxState Chatbox => `Sessions.ActiveChatbox`;
    ChatboxMessage Message => `Chatbox.Message`;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        <Grid RowDefinitions=<>
            <RowDefinition Height=Auto />
            <RowDefinition Height=Auto />
            <RowDefinition Height=Auto />
        </>>
            if (`Message.Images.Count > 0`)
                <ScrollViewer Grid.Row=0 MaxHeight=96 Padding=`new Thickness(16, 0, 16, 0)`
                    HorizontalScrollBarVisibility=Auto VerticalScrollBarVisibility=Disabled>
                    <StackPanel Orientation=Horizontal>
                        foreach (var a in `Message.Images`)
                        {
                            <Grid Margin=`new Thickness(0, 4, 8, 4)`>
                                <Border Width=64 Height=64 CornerRadius=6 BorderBrush=`theme.CardStroke`
                                        BorderThickness=`new Thickness(1)` Background=`theme.CardBackground`
                                        VerticalAlignment=Top>
                                    <Image Source=`a.Preview` Stretch=Uniform Margin=2 />
                                </Border>
                                <Button Width=18 Height=18 Padding=0 HorizontalAlignment=Right VerticalAlignment=Top
                                        CornerRadius=9 Foreground=`theme.PrimaryText` FontSize=10
                                        ToolTipService.ToolTip="Remove attachment" @Click+=`Message.Images.Remove(a)`>
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
                suggestBox = <SuggestBox PlaceholderText=`ShellMode ? "Run a shell command… (e.g. git status)" : "Message OpenCode..."` IsEnabled=`IsEnabled`
                    TextChanged+=`OnInputTextChanged` PreviewKeyDown+=`OnPreviewKeyDown` SubmitRequested+=`OnSubmitRequested` />
                <StackPanel Grid.Column=1 Orientation=Horizontal Spacing=8 VerticalAlignment=Bottom>
                    if (`!ShellMode`)
                        <Button ToolTipService.ToolTip="Attach image" CornerRadius=6 IsEnabled=`IsEnabled`
                                @Click+=`OnPickImages()`>
                            <SymbolIcon Symbol=Camera VerticalAlignment=Center />
                        </Button>
                    if (`Chatbox.PendingPromptsCount > 0`)
                        <Border Background=`theme.SystemCautionBackground` CornerRadius=6 Padding=`new Thickness(8, 4, 8, 4)` VerticalAlignment=Center>
                            <TextBlock Text=`$"⏳ {Chatbox.PendingPromptsCount} queued"` FontSize=11 Foreground=`theme.SystemCaution` VerticalAlignment=Center />
                        </Border>
                    if (`IsBusy`)
                        <Button Content="⏹ Stop" @Click+=`await InterruptSessionAsync()` CornerRadius=6 />
                    <SendMessageButton Mode=`SendMode` IsBusy=`IsBusy` Enabled=`IsEnabled`
                                       SendRequested+=`OnSendWithMode` />
                </StackPanel>
            </Grid>
            if (`!ShellMode`)
                <StackPanel Grid.Row=2 Orientation=Horizontal Spacing=`IsCompact ? 8 : 12` Padding=`new Thickness(IsCompact ? 12 : 16, 0, IsCompact ? 12 : 16, 10)`>
                    <StackPanel Orientation=Horizontal Spacing=6 VerticalAlignment=Center>
                        <TextBlock Text="Mode" FontSize=10 Foreground=`theme.SecondaryText` VerticalAlignment=Center Visibility=`IsCompact ? Visibility.Collapsed : Visibility.Visible` />
                        modeCombo = <ComboBox
                            ItemsSource=`Models.AgentOptions.ToArray()`
                            SelectedItem=`Sessions.ActiveChatParams.Agent ?? Models.AgentOptions.FirstOrDefault()`
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
                            ItemsSource=`SelectionVariants.Prepend(null)`
                            SelectedItem=`Sessions.ActiveChatParams.Variant`
                            IsEnabled=`SelectionVariants.Count > 0`
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
partial class ChatComposer : IQuickMarkupComponent<Grid>
{
    static readonly IReadOnlyList<string> EmptyList = [];
    private DispatcherQueue? _dispatcher;

    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        SendMode = SettingsStore.SendMode.ToString();
        SettingsStore.Changed += OnSettingsChanged;

        suggestBox.Providers =
        [
            new BuiltInCommandSuggestionProvider(IsBuiltInAvailable),
            new ServerCommandSuggestionProvider(() => Opencode, () => Sessions.ActiveSessionDirectory),
            new ServerSkillSuggestionProvider(() => Opencode, () => Sessions.ActiveSessionDirectory),
            new ServerFileSuggestionProvider(() => Opencode, () => Sessions.ActiveSessionDirectory),
        ];
        suggestBox.CommandTriggered += (sender, item) => RunBuiltInCommandAsync(item.Action!);

        suggestBox.MarkupNode.Focus(FocusState.Programmatic);
        WatchMessage();
    }

    void WatchMessage()
    {
        var message = Message;
        MessageComp.Watch(newMessage =>
        {
            suggestBox.SwapText(newMessage.Text, out var oldText);
            message.Text = oldText;
            message = newMessage;
        });

    }

    private void OnSettingsChanged()
    {
        _ = _dispatcher?.RunOrEnqueue(() => SendMode = SettingsStore.SendMode.ToString());
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
            e.Handled = true;
            var images = await ImageIOHelper.PasteImageFromClipboardAsync();
            if (images.Count > 0)
            {
                Message.Images.AddRange(images);
            } else
            {
#if WASDK
                suggestBox.PasteFromClipboard();
#endif
            }
        }
    }
    private async void OnPickImages()
    {
        var images = await ImageIOHelper.PickImagesAsync(HostWindow);
        Message.Images.AddRange(images);
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!ShellMode && (suggestBox.MarkupNode.Text ?? "") == "!")
            EnterShellMode();
    }

    private void EnterShellMode()
    {
        ShellMode = true;
        suggestBox.Clear();
        SetSuggestionPrefixes("");
        suggestBox.MarkupNode.Focus(FocusState.Programmatic);
    }

    private void ExitShellMode()
    {
        if (!ShellMode) return;
        ShellMode = false;
        suggestBox.Clear();
        SetSuggestionPrefixes("/@");
        suggestBox.MarkupNode.Focus(FocusState.Programmatic);
    }

    private void SetSuggestionPrefixes(string prefixes)
    {
        var providers = suggestBox.Providers;
        suggestBox.Prefixes = prefixes;
        suggestBox.Providers = providers;
    }

    private async Task SubmitShellAsync()
    {
        var command = (suggestBox.MarkupNode.Text ?? "").Trim();
        ExitShellMode();
        if (command.Length == 0) return;
        await SendShellCommandAsync(command);
    }

    private async Task OnSubmitRequested(SuggestBox sender, string text)
    {
        if (ShellMode)
        {
            await SubmitShellAsync();
            return;
        }
        if (await TryRunBuiltInTextAsync(text)) return;
        Message.Text = text;
        await SendAsync(null);
        sender.Clear();
    }

    private async Task OnSendWithMode(SendPromptMode mode)
    {
        if (ShellMode)
        {
            await SubmitShellAsync();
            return;
        }
        if (await TryRunBuiltInTextAsync(suggestBox.MarkupNode.Text)) return;
        Message.Text = suggestBox.MarkupNode.Text;
        await SendAsync(mode);
        suggestBox.Clear();
    }

    private bool IsBuiltInAvailable(string name) => name != "interrupt" || IsBusy;

    private async Task RunBuiltInCommandAsync(string name)
    {
        switch (name)
        {
            case "agents":
                OpenCombo(modeCombo);
                break;
            case "connect":
                await ProviderConnectDialog.ShowAsync(Opencode, Toasts, Models, MarkupNode.XamlRoot!);
                break;
            case "continue":
                await EnsureActiveCurrentSession();
                HandleSentStatus(await Chatbox.SendManualContinueAsync());
                break;
            case "editor":
                LaunchFolder(FolderLauncherHelper.OpenInEditor);
                break;
            case "explorer":
                LaunchFolder(FolderLauncherHelper.OpenInFileManager);
                break;
            case "fork":
                if (Sessions.ActiveSessionId is {} sessionId)
                    UIs.ForkAndSwitchSession(sessionId);
                else
                    Toasts.ShowWarning("No session to fork.", "/fork");
                break;
            case "interrupt":
                if (!await InterruptSessionAsync())
                {
                    Toasts.ShowWarning("Chat is not running currently.", "/interrupt");
                }
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
                if (Sessions.ActiveSessionId is null)
                    Toasts.ShowWarning("There is no conversation to rename yet.", "/rename");
                else
                    UIs.BeginRenameAndFocus();
                break;
            case "setting":
                SettingsOpen = true;
                break;
            case "terminal":
                LaunchFolder(FolderLauncherHelper.OpenInTerminal);
                break;
            case "undo":
                await ChatP.UndoLastAsync();
                break;
            case "variants":
                if (SelectionVariants.Count is 0)
                    Toasts.ShowWarning("The selected model has no reasoning variants.", "No variants");
                else
                    OpenCombo(variantCombo);
                break;
        }
    }

    async Task EnsureActiveCurrentSession()
    {
        var chatbox = Chatbox;
        ChatMessagesState resolved;
        if (chatbox.SessionId is not {} sess)
        {
            sess = (await Sessions.CreateFromPreparedSessionAsync()).Id;
        }
        resolved = await ChatStateAsync.AwaitResultAsync();
        if (resolved?.SessionId != sess)
            throw new InvalidOperationException($"Session id not the same {resolved?.SessionId ?? "null"} != {sess}");
        if (chatbox != Chatbox)
        {
            Chatbox.Message = chatbox.Message;
            chatbox.Message = new();
        }
    }

    private async Task<bool> InterruptSessionAsync()
    {
        if (Sessions.ActiveHead is {} activeSession && activeSession.IsBusy)
        {
            await Opencode.AbortAsync(activeSession.Id);
            return true;
        }
        return false;
    }

    private async Task SendAsync(SendPromptMode? mode)
    {
        await EnsureActiveCurrentSession();
        HandleSentStatus(await Chatbox.SendAsync(mode));
    }

    private async Task SendShellCommandAsync(string command)
    {
        await EnsureActiveCurrentSession();
        HandleSentStatus(await Chatbox.SendShellAsync(command));
    }
    void HandleSentStatus(ChatboxSentStatus sentStatus)
    {
        switch (sentStatus)
        {
            case ChatboxSentStatus.Sent:
            case ChatboxSentStatus.Queued:
                UIs.ScrollChatToBottom();
                return;
            case ChatboxSentStatus.Error:
            case ChatboxSentStatus.Empty:
            default:
                return;
        }
    }

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
        if (ShellMode) ExitShellMode();
        suggestBox.MarkupNode.Text = txt;
    }
}
