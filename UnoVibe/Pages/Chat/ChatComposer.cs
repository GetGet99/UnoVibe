using Microsoft.UI.Input;
using UnoVibe.Controls;
using UnoVibe.Models;
using Uno.Extensions;
namespace UnoVibe.Pages.Chat;

[QuickMarkup("""
    using UnoVibe.Controls;
    using UnoVibe.Models;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Microsoft.UI;
    required ChatboxState Chatbox;
    required ChatParameters ChatParams;
    required string SessionDirectory;
    required SessionHead? Head;
    inject Window HostWindow;
    inject bool IsCompact;
    inject bool SettingsOpen;
    inject `UnoVibe.Integration.OpencodeClient` Opencode;
    inject ToastsProvider Toasts;
    inject UIServiceProvider UIs;
    inject ModelsProvider Models;
    string SendMode = "";
    `IReadOnlyList<string>` SelectionVariants => `
        ChatParams.Model is not {} model
        ? EmptyList
        :  (Models.ModelOptions.TryGetValue(model, out var modelOption)
            ? modelOption.Variants
            : EmptyList
        )`;
    bool IsBusy => `Head?.IsBusy ?? false`;
    private bool IsEnabled = true;
    Thickness ComposerPadding = `new Thickness(0)`;
    Thickness InputRowPadding => `new Thickness(0, 0, 0, IsCompact ? 12 : 16)`;
    ChatboxMessage Message => `Chatbox.Message`;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        <StackPanel Padding=`ComposerPadding`>
            if (`Message.Images.Count > 0 || Message.Files.Count > 0`)
                <StackPanel Grid.Row=0 Spacing=6 Padding=`new Thickness(0, 0, 0, 8)`>
                    if (`Message.Images.Count > 0`)
                        <ScrollViewer MaxHeight=96 HorizontalScrollBarVisibility=Auto VerticalScrollBarVisibility=Disabled>
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
                    if (`Message.Files.Count > 0`)
                        <ScrollViewer MaxHeight=96 HorizontalScrollBarVisibility=Auto VerticalScrollBarVisibility=Disabled>
                            <StackPanel Orientation=Horizontal>
                                foreach (var f in `Message.Files`; `f.Id`)
                                {
                                    <Border Margin=`new Thickness(0, 4, 8, 4)` Background=`theme.CardBackground`
                                            BorderBrush=`theme.CardStroke` BorderThickness=`new Thickness(1)` CornerRadius=6
                                            Padding=`new Thickness(10, 0, 6, 0)` Height=32>
                                        <StackPanel Orientation=Horizontal Spacing=8 VerticalAlignment=Center>
                                            <TextBlock Text=`f.FileName` FontSize=12 Foreground=`theme.PrimaryText`
                                                    VerticalAlignment=Center MaxWidth=220 TextTrimming=`TextTrimming.CharacterEllipsis` />
                                            <Button Width=18 Height=18 Padding=0 CornerRadius=9 Foreground=`theme.PrimaryText`
                                                    FontSize=10 ToolTipService.ToolTip="Remove attachment"
                                                    @Click+=`Message.Files.Remove(f)`>
                                                <TextBlock Text="✕" FontSize=10 />
                                            </Button>
                                        </StackPanel>
                                    </Border>
                                }
                            </StackPanel>
                        </ScrollViewer>
                </StackPanel>
            <Grid Grid.Row=1 ColumnSpacing=`IsCompact ? 6 : 8` Padding=`InputRowPadding` ColumnDefinitions=<>
                <ColumnDefinition />
                <ColumnDefinition Width=Auto />
            </>>
                suggestBox = <SuggestBox PlaceholderText=`Message.ShellMode ? "Run a shell command… (e.g. git status)" : "Message OpenCode..."` IsEnabled=`IsEnabled`
                    TextChanged+=`OnInputTextChanged` PreviewKeyDown+=`OnPreviewKeyDown` SubmitRequested+=`OnSubmitRequested`
                    FileCommitRequested+=`OnFileCommitRequested` />
                <StackPanel Grid.Column=1 Orientation=Horizontal Spacing=8 VerticalAlignment=Bottom>
                    if (`Chatbox.PendingPromptsCount > 0`)
                        <Border Background=`theme.SystemCautionBackground` CornerRadius=6 Padding=`new Thickness(8, 4, 8, 4)` VerticalAlignment=Center>
                            <TextBlock Text=`$"⏳ {Chatbox.PendingPromptsCount} queued"` FontSize=11 Foreground=`theme.SystemCaution` VerticalAlignment=Center />
                        </Border>
                    <SendMessageButton Mode=`SendMode` IsBusy=`IsBusy` Enabled=`IsEnabled`
                                       SendRequested+=`OnSendWithMode` />
                </StackPanel>
            </Grid>
            <StackPanel Orientation=Horizontal Spacing=`IsCompact ? 8 : 12` Padding=0>
                if (`Message.ShellMode`) {
                    <TextBlock Text="! Shell mode"
                            FontSize=11 Foreground=`theme.SecondaryText` VerticalAlignment=Center TextTrimming=`TextTrimming.CharacterEllipsis` />
                    <Button Content="Exit shell mode (Esc)" CornerRadius=6 Padding=`new Thickness(10, 3, 10, 3)` @Click+=`ExitShellMode()` />
                } else {
                    <StackPanel Orientation=Horizontal Spacing=6 VerticalAlignment=Center>
                        <TextBlock Text="Mode" FontSize=10 Foreground=`theme.SecondaryText` VerticalAlignment=Center Visibility=`IsCompact ? Visibility.Collapsed : Visibility.Visible` />
                        modeCombo = <ComboBox
                            ItemsSource=`Models.AgentOptions.ToArray()`
                            SelectedItem=`ChatParams.Agent ?? Models.AgentOptions.FirstOrDefault()`
                            ItemTemplate=template (string? value) { <TextBlock Text=`Capitalize(value) ?? "Build"` /> }
                            SelectedItem+=>`x => ChatParams.Agent = x as string`
                            MinWidth=`IsCompact ? 76 : 90`
                            Height=28
                            FontSize=12
                        />
                    </StackPanel>
                    <StackPanel Orientation=Horizontal Spacing=6 VerticalAlignment=Center>
                        <TextBlock Text="Model" FontSize=10 Foreground=`theme.SecondaryText` VerticalAlignment=Center Visibility=`IsCompact ? Visibility.Collapsed : Visibility.Visible` />
                        modelPicker = <ModelPicker ChatParams=`ChatParams` Head=`Head` />
                    </StackPanel>
                    <StackPanel Orientation=Horizontal Spacing=6 VerticalAlignment=Center>
                        <TextBlock Text="Variant" FontSize=10 Foreground=`theme.SecondaryText` VerticalAlignment=Center Visibility=`IsCompact ? Visibility.Collapsed : Visibility.Visible` />
                        variantCombo = <ComboBox
                            ItemsSource=`SelectionVariants.Prepend(null)`
                            SelectedItem=`ChatParams.Variant`
                            IsEnabled=`SelectionVariants.Count > 0`
                            ItemTemplate=template (string? value) { <TextBlock Text=`Capitalize(value) ?? "Default"` /> }
                            SelectedItem+=>`x => ChatParams.Variant = x as string`
                            MinWidth=`IsCompact ? 76 : 90`
                            Height=28
                            FontSize=12
                        />
                    </StackPanel>
                    <Button
                        Grid.Column=1
                        ToolTipService.ToolTip="Attach image"
                        Padding=`new Thickness(5, 0, 5, 0)`
                        CornerRadius=4
                        IsEnabled=`IsEnabled`
                        Height=28
                        FontSize=12
                        @Click+=`OnPickImages()`
                    >
                        <SymbolIcon Symbol=Camera VerticalAlignment=Center />
                    </Button>
                }
                if (`IsBusy`)
                    <Button Content="⏹ Stop" @Click+=`await InterruptSessionAsync()` CornerRadius=6 />
            </StackPanel>
        </StackPanel>
    </root>
    """)]
partial class ChatComposer : IQuickMarkupComponent<StackPanel>
{
    public event Action? Sent;
    public event Func<Task>? UndoRequested;
    public event Func<Task>? RedoRequested;
    public event Action? NewSessionRequested;
    public event Action? BeginRenameRequested;
    public event Func<Task<ChatboxState>>? EnsureSessionRequested;
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
            new ServerCommandSuggestionProvider(() => Opencode, () => SessionDirectory),
            new ServerSkillSuggestionProvider(() => Opencode, () => SessionDirectory),
            new ServerFileSuggestionProvider(() => Opencode, () => SessionDirectory),
        ];
        suggestBox.CommandTriggered += (sender, item) => RunBuiltInCommandAsync(item.Action!);

        suggestBox.Text = Message.Text;
        if (Message.ShellMode) SetSuggestionPrefixes("");
        suggestBox.FocusInput();
        WatchMessage();
    }

    void WatchMessage()
    {
        MessageComp.Watch(newMessage =>
        {
            if (!suggestBox.IsFocused)
                suggestBox.Text = newMessage.Text;
        });
    }

    private void OnSettingsChanged()
    {
        _ = _dispatcher?.RunOrEnqueue(() => SendMode = SettingsStore.SendMode.ToString());
    }

    private async void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Message.ShellMode && e.Key == Windows.System.VirtualKey.Escape && !e.Handled)
        {
            e.Handled = true;
            ExitShellMode();
            return;
        }

        if (e.Key == Windows.System.VirtualKey.V &&
            InputKeyboardSource.GetCModifierKeyStateForCurrentThread()
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) &&
            !InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            var images = await ImageIOHelper.PasteImageFromClipboardAsync();
            if (images.Count > 0)
            {
                e.Handled = true;
                Message.Images.AddRange(images);
            }
        }
    }
    private async void OnPickImages()
    {
        var images = await ImageIOHelper.PickImagesAsync(HostWindow);
        Message.Images.AddRange(images);
    }

    private async Task OnFileCommitRequested(SuggestBox sender, SuggestionItem item)
    {
        if (string.IsNullOrEmpty(item.Text)) return;
        Message.Files.Add(new FileAttachment
        {
            Path = System.IO.Path.GetFullPath(System.IO.Path.Combine(SessionDirectory, item.Text)),
        });
    }

    private void OnInputTextChanged(object sender, RoutedEventArgs e)
    {
        Message.Text = suggestBox.Text;
        if (!Message.ShellMode && suggestBox.Text == "!")
            EnterShellMode();
    }

    private void EnterShellMode()
    {
        Message.ShellMode = true;
        suggestBox.Clear();
        SetSuggestionPrefixes("");
        suggestBox.FocusInput();
    }

    private void ExitShellMode()
    {
        if (!Message.ShellMode) return;
        Message.ShellMode = false;
        suggestBox.ClearAfterSubmit();
        SetSuggestionPrefixes("/@");
        suggestBox.FocusInput();
    }

    private void SetSuggestionPrefixes(string prefixes)
    {
        var providers = suggestBox.Providers;
        suggestBox.Prefixes = prefixes;
        suggestBox.Providers = providers;
    }

    private async Task SubmitShellAsync()
    {
        var command = suggestBox.Text.Trim();
        ExitShellMode();
        if (command.Length == 0) return;
        await SendShellCommandAsync(command);
    }

    private async Task OnSubmitRequested(SuggestBox sender, string text)
    {
        if (Message.ShellMode)
        {
            await SubmitShellAsync();
            return;
        }
        if (await TryRunBuiltInTextAsync(text)) return;
        await SendAsync(null);
        sender.ClearAfterSubmit();
    }

    private async Task OnSendWithMode(SendPromptMode mode)
    {
        if (Message.ShellMode)
        {
            await SubmitShellAsync();
            return;
        }
        if (await TryRunBuiltInTextAsync(suggestBox.Text)) return;
        await SendAsync(mode);
        suggestBox.ClearAfterSubmit();
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
                await EnsureSessionAndSyncAsync();
                HandleSentStatus(await Chatbox.SendManualContinueAsync());
                break;
            case "editor":
                LaunchFolder(FolderLauncherHelper.OpenInEditor);
                break;
            case "explorer":
                LaunchFolder(FolderLauncherHelper.OpenInFileManager);
                break;
            case "fork":
                if (Chatbox.SessionId is {} sessionId)
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
                if (NewSessionRequested is null)
                    Toasts.ShowWarning("Open the chat to start a new session.", "/new");
                else
                    NewSessionRequested();
                break;
            case "redo":
                if (RedoRequested is null)
                    Toasts.ShowWarning("Open the session to redo the last message.", "/redo");
                else
                    await RedoRequested();
                break;
            case "rename":
                if (Chatbox.SessionId is null)
                    Toasts.ShowWarning("There is no conversation to rename yet.", "/rename");
                else if (BeginRenameRequested is null)
                    Toasts.ShowWarning("Open the session to rename it.", "/rename");
                else
                    BeginRenameRequested();
                break;
            case "setting":
                SettingsOpen = true;
                break;
            case "terminal":
                LaunchFolder(FolderLauncherHelper.OpenInTerminal);
                break;
            case "undo":
                if (UndoRequested is null)
                    Toasts.ShowWarning("Open the session to undo the last message.", "/undo");
                else
                    await UndoRequested();
                break;
            case "variants":
                if (SelectionVariants.Count is 0)
                    Toasts.ShowWarning("The selected model has no reasoning variants.", "No variants");
                else
                    OpenCombo(variantCombo);
                break;
        }
    }

    async Task EnsureSessionAndSyncAsync()
    {
        if (EnsureSessionRequested is null) return;
        Chatbox = await EnsureSessionRequested();
    }

    private async Task<bool> InterruptSessionAsync()
    {
        if (Head is {} activeSession && activeSession.IsBusy)
        {
            await Opencode.AbortAsync(activeSession.Id);
            return true;
        }
        return false;
    }

    private async Task SendAsync(SendPromptMode? mode)
    {
        await EnsureSessionAndSyncAsync();
        HandleSentStatus(await Chatbox.SendAsync(mode));
    }

    private async Task SendShellCommandAsync(string command)
    {
        await EnsureSessionAndSyncAsync();
        HandleSentStatus(await Chatbox.SendShellAsync(command));
    }
    void HandleSentStatus(ChatboxSentStatus sentStatus)
    {
        switch (sentStatus)
        {
            case ChatboxSentStatus.Sent:
            case ChatboxSentStatus.Queued:
                Sent?.Invoke();
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
        suggestBox.ClearAfterSubmit();
        return true;
    }

    private static void OpenCombo(ComboBox? combo)
    {
        if (combo is not null) combo.IsDropDownOpen = true;
    }

    private void LaunchFolder(Func<string, string?> open)
    {
        var error = open(SessionDirectory);
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
}
