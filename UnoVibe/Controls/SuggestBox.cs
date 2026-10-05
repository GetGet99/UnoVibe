using Microsoft.UI.Input;

namespace UnoVibe.Controls;

[QuickMarkup("""
    using QuickMarkup.WinUI;
    using Microsoft.UI;
    using Microsoft.UI.Xaml.Controls.Primitives;
    int SelectedIndex = -1;
    string Prefixes = "/@";
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        input = <TextBox TextWrapping=Wrap AcceptsReturn=true MinHeight=36 MaxHeight=120 PreviewKeyDown+=`OnPreviewKeyDown` TextChanged+=`OnTextChanged`
            FlyoutBase.AttachedFlyout=suggestFlyout = <Flyout Placement=Top ShowMode=Transient Closed+=`OnSuggestFlyoutClosed`>
            <Border Padding=4 MinWidth=320 MaxWidth=480
                    GotFocus+=`OnSuggestionsGotFocus`>
                <ScrollViewer MaxHeight=280>
                    <StackPanel>
                        foreach (index; var item in `_items`; `item.Key`)
                        {
                            <Button Padding=`new Thickness(10,  6, 10,  6)` HorizontalContentAlignment=Left
                                    Background=`index == SelectedIndex ? theme.SubtleFill : new SolidColorBrush(Colors.Transparent)`
                                    BorderThickness=0 CornerRadius=6
                                    @Click+=`await CommitSuggestionAsync(item)`
                                    HorizontalAlignment=Stretch
                            >
                                <StackPanel Orientation=Horizontal Spacing=8>
                                    <Border Background=`KindBadgeBrush(item.Kind)` CornerRadius=3 Padding=`new Thickness(5, 1, 5, 2)` VerticalAlignment=Center>
                                        <TextBlock Text=`item.KindLabel` FontSize=10 Foreground=`AppTheme.TextOnAccent` FontWeight=`FontWeights.SemiBold` />
                                    </Border>
                                    <TextBlock Text=`item.Text` FontSize=12 VerticalAlignment=Center />
                                    if (`item.Detail.Length > 0`)
                                        <TextBlock Text=`item.Detail` FontSize=11 Foreground=`theme.SecondaryText` TextTrimming=`TextTrimming.CharacterEllipsis` MaxWidth=280 VerticalAlignment=Center />
                                </StackPanel>
                            </Button>
                        }
                    </StackPanel>
                </ScrollViewer>
            </Border>
        </Flyout> />
    </root>
    """)]
partial class SuggestBox : IQuickMarkupComponent<TextBox>
{
    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();
        suggestFlyout.FlyoutPresenterStyle = new()
        {
            BasedOn = (Style)
#if WASDK
            App.Current.Resources["DefaultFlyoutPresenterStyle"]
#else
            App.Current.Resources["DefaultFlyoutPresenter"]
#endif
            ,
            Setters =
            {
                new Setter { Property = Control.PaddingProperty, Value = new Thickness() },
                new Setter { Property = Control.CornerRadiusProperty, Value = new CornerRadius(8) }
            }
        };
    }
    public delegate Task SubmitHandler(SuggestBox sender, string text);

    public event SubmitHandler? SubmitRequested;

    public delegate Task CommandTriggeredHandler(SuggestBox sender, SuggestionItem item);

    public event CommandTriggeredHandler? CommandTriggered;

    private readonly ObservableCollection<SuggestionItem> _items = new();

    private int _suggestSeq;

    private bool _suppressArrowSelection;
    private bool _blockStrayTextChange;
    private bool _programmaticTextChange;
    private bool _inputGuardsAttached;

    private IReadOnlyList<ISuggestionProvider>? _providers;
    private SuggestionBoxController? _controller;

    public IReadOnlyList<ISuggestionProvider>? Providers
    {
        get => _providers;
        set
        {
            _providers = value;
            _controller = null;
        }
    }

    private SuggestionBoxController Controller =>
        _controller ??= new SuggestionBoxController(Providers ?? Array.Empty<ISuggestionProvider>(), Prefixes);

    public void Clear() => _ = SetTextProgrammaticallyAsync("");

    public void SwapText(string newText, out string oldText)
    {
        oldText = input.Text;
        _ = SetTextProgrammaticallyAsync(newText);
    }

    private async Task SetTextProgrammaticallyAsync(string newText)
    {
        if (input is null) return;
        _programmaticTextChange = true;
        try
        {
            input.Text = newText;
        }
        finally
        {
            _programmaticTextChange = false;
        }
        input.AcceptsReturn = false;
        await Task.Delay(16);
        input.AcceptsReturn = true;
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e) => _ = UpdateSuggestionsAsync();

    private async Task UpdateSuggestionsAsync()
    {
        var seq = ++_suggestSeq;
        await Task.Delay(60);
        if (seq != _suggestSeq) return;

        var text = input.Text;
        var caret = input.SelectionStart;
        if (Controller.TryGetQuery(text, caret, out var trigger, out var query, out var tokenStart))
        {
            IReadOnlyList<SuggestionItem> items;
            try
            {
                items = await Controller.GetSuggestionsAsync(trigger, query, tokenStart == 0);
            }
            catch
            {
                items = Array.Empty<SuggestionItem>();
            }
            if (seq != _suggestSeq) return;
            ShowSuggestions(items);
        }
        else
        {
            CloseSuggestions();
        }
    }

    private void ShowSuggestions(IReadOnlyList<SuggestionItem> items)
    {
        if (items.Count == 0 || input is null || suggestFlyout is null)
        {
            CloseSuggestions();
            return;
        }
        _items.Clear();
        foreach (var item in items) _items.Add(item);
        SelectedIndex = 0;
        if (!suggestFlyout.IsOpen) suggestFlyout.ShowAt(input);
    }

    private void CloseSuggestions()
    {
        if (suggestFlyout is { IsOpen: true }) suggestFlyout.Hide();
        _items.Clear();
        SelectedIndex = -1;
    }

    private void OnSuggestFlyoutClosed(object? sender, object e)
    {
        SelectedIndex = -1;
        _items.Clear();
    }

    private void OnSuggestionsGotFocus(object sender, RoutedEventArgs e) => input?.Focus(FocusState.Programmatic);

    private bool HandleSuggestionKey(KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Up:
                e.Handled = true;
                _suppressArrowSelection = true;
                _ = input?.DispatcherQueue?.TryEnqueue(() => _suppressArrowSelection = false);
                SelectedIndex = SelectedIndex <= 0 ? _items.Count - 1 : SelectedIndex - 1;
                return true;
            case Windows.System.VirtualKey.Down:
                e.Handled = true;
                _suppressArrowSelection = true;
                _ = input?.DispatcherQueue?.TryEnqueue(() => _suppressArrowSelection = false);
                SelectedIndex = SelectedIndex >= _items.Count - 1 ? 0 : SelectedIndex + 1;
                return true;
            case Windows.System.VirtualKey.Enter:
                if (InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
                    return false;
                e.Handled = true;
                BlockStrayTextChange();
                if (SelectedIndex >= 0 && SelectedIndex < _items.Count)
                    CommitSuggestion(_items[SelectedIndex]);
                else
                    CloseSuggestions();
                return true;
            case Windows.System.VirtualKey.Tab:
                e.Handled = true;
                BlockStrayTextChange();
                if (SelectedIndex >= 0 && SelectedIndex < _items.Count)
                    CommitSuggestion(_items[SelectedIndex]);
                else
                    CloseSuggestions();
                return true;
            case Windows.System.VirtualKey.Escape:
                e.Handled = true;
                CloseSuggestions();
                return true;
        }
        return false;
    }

    private async Task CommitSuggestionAsync(SuggestionItem item) => CommitSuggestion(item);

    private void CommitSuggestion(SuggestionItem item)
    {
        if (input is null) return;

        if (item.Action is not null)
        {
            CloseSuggestions();
            _ = SetTextProgrammaticallyAsync("");
            input.Focus(FocusState.Programmatic);
            if (CommandTriggered is { } handler)
                _ = handler(this, item);
            return;
        }

        var text = input.Text;
        var caret = input.SelectionStart;
        if (!Controller.TryGetQuery(text, caret, out _, out _, out var tokenStart)) return;

        var newText = text.Remove(tokenStart, caret - tokenStart).Insert(tokenStart, item.Insert);
        _programmaticTextChange = true;
        try
        {
            input.Text = newText;
            input.SelectionStart = tokenStart + item.Insert.Length;
        }
        finally
        {
            _programmaticTextChange = false;
        }
        CloseSuggestions();
        input.Focus(FocusState.Programmatic);
    }

    private static Brush? KindBadgeBrush(string kind) => kind switch
    {
        "skill" => ThemeBrushes.Global.SystemCaution,
        "file" => ThemeBrushes.Global.SystemSuccess,
        "agent" => ThemeBrushes.Global.SystemAttention,
        "builtin" => ThemeBrushes.Global.SecondaryText,
        _ => ThemeBrushes.Global.Accent,
    };

    private void EnsureInputGuards()
    {
        if (_inputGuardsAttached || input is null) return;
        _inputGuardsAttached = true;
        input.SelectionChanging += OnSelectionChanging;
        input.BeforeTextChanging += OnBeforeTextChanging;
    }

    private void OnSelectionChanging(TextBox sender, TextBoxSelectionChangingEventArgs e)
    {
        if (_suppressArrowSelection)
        {
            _suppressArrowSelection = false;
            e.Cancel = true;
        }
    }

    private void OnBeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs e)
    {
        if (_blockStrayTextChange && !_programmaticTextChange)
            e.Cancel = true;
    }

    private void BlockStrayTextChange()
    {
        _blockStrayTextChange = true;
        _ = input?.DispatcherQueue?.TryEnqueue(() => _blockStrayTextChange = false);
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        EnsureInputGuards();
        if (suggestFlyout is { IsOpen: true } && _items.Count > 0 && HandleSuggestionKey(e))
            return;

        if (e.Key != Windows.System.VirtualKey.Enter) return;
        if (InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            return;
        e.Handled = true;
        BlockStrayTextChange();

        var text = input.Text;
        if (SubmitRequested is { } handler)
            _ = handler(this, text);
    }
    public void PasteFromClipboard() => input.PasteFromClipboard();
}
