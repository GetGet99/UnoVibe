using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Collections.ObjectModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace UnoVibe.Controls;

[QuickMarkup("""
    using QuickMarkup.WinUI;
    using Microsoft.UI;
    int SelectedIndex = -1;
    bool SuggestionsOpen = false;
    double SuggestionHeight = 0;
    string Prefixes = "/@";
    string PlaceholderText = "";
    bool IsEnabled = true;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        <Grid RowDefinitions=<>
            <RowDefinition Height=Auto />
            <RowDefinition Height=`new GridLength(0)` />
        </>>
            input = <RichEditBox TextWrapping=Wrap AcceptsReturn=true MinHeight=36 MaxHeight=120
                PlaceholderText=`PlaceholderText` IsEnabled=`IsEnabled`
                PreviewKeyDown+=`OnInnerPreviewKeyDown` TextChanged+=`OnInnerTextChanged`
                SelectionChanged+=`OnSelectionChanged` SelectionChanging+=`OnSelectionChanging` Paste+=`OnPaste`
                ProcessKeyboardAccelerators+=`OnProcessKeyboardAccelerators` />
            suggestionsContainer = <Border Grid.Row=1 Visibility=`SuggestionsOpen ? Visibility.Visible : Visibility.Collapsed`
                VerticalAlignment=Top HorizontalAlignment=Stretch Canvas.ZIndex=10 Height=`SuggestionHeight`
                Padding=4 MinWidth=320 MaxWidth=480 CornerRadius=8
                Background=`theme.SolidBackground` BorderBrush=`theme.DividerStroke` BorderThickness=`new Thickness(1)`>
                <ScrollViewer MaxHeight=280>
                    <StackPanel>
                        foreach (index; var item in `_items`; `item.Key`)
                        {
                            <Button Padding=`new Thickness(10, 6, 10, 6)` HorizontalContentAlignment=Left
                                    Background=`index == SelectedIndex ? theme.SubtleFill : new SolidColorBrush(Colors.Transparent)`
                                    BorderThickness=0 CornerRadius=6
                                    @Click+=`await CommitSuggestionAsync(item)`
                                    HorizontalAlignment=Stretch>
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
        </Grid>
    </root>
    """)]
partial class SuggestBox : IQuickMarkupComponent<Grid>
{
    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();
    }
    public delegate Task SubmitHandler(SuggestBox sender, string text);

    public event SubmitHandler? SubmitRequested;

    public delegate Task CommandTriggeredHandler(SuggestBox sender, SuggestionItem item);

    public event CommandTriggeredHandler? CommandTriggered;

    public delegate Task FileCommitHandler(SuggestBox sender, SuggestionItem item);

    public event FileCommitHandler? FileCommitRequested;

    public event RoutedEventHandler? TextChanged;

    public event KeyEventHandler? PreviewKeyDown;

    private readonly ObservableCollection<SuggestionItem> _items = new();

    private int _suggestSeq;

    private bool _programmaticTextChange;

    private int _caret;

    private bool _suppressArrowSelection;

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

    public string Text
    {
        get => GetPlainText();
        set => SetDraftText(value);
    }

    public bool IsFocused => input?.FocusState != FocusState.Unfocused;

    public void FocusInput() => input?.Focus(FocusState.Programmatic);

    public void Clear() => SetDraftText("");

    public void ClearAfterSubmit() => _ = SetTextWithEnterSuppressionAsync("");

    private void SetDraftText(string newText)
    {
        if (input is null) return;
        _programmaticTextChange = true;
        try
        {
            SetPlainText(newText);
        }
        finally
        {
            _programmaticTextChange = false;
        }
        CloseSuggestions();
    }

    private async Task SetTextWithEnterSuppressionAsync(string newText)
    {
        if (input is null) return;
        _programmaticTextChange = true;
        try
        {
            SetPlainText(newText);
        }
        finally
        {
            _programmaticTextChange = false;
        }
        CloseSuggestions();
        input.AcceptsReturn = false;
        await Task.Delay(16);
        if (input is not null) input.AcceptsReturn = true;
    }

    private string GetPlainText()
    {
        if (input?.TextDocument is { } doc)
        {
            doc.GetText(TextGetOptions.NoHidden, out var text);
            return text ?? "";
        }
        return "";
    }

    private void SetPlainText(string value, int caret = -1)
    {
        if (input?.TextDocument is { } doc)
        {
            doc.SetText(TextSetOptions.None, value);
            TrySetCaret(caret >= 0 ? caret : value.Length);
        }
    }

    private void TrySetCaret(int index)
    {
        if (input?.TextDocument is not { } doc) return;
        try
        {
#pragma warning disable Uno0001
            doc.Selection.StartPosition = Math.Clamp(index, 0, GetPlainText().Length);
#pragma warning restore Uno0001
        }
        catch
        {
        }
    }

    private void ReplaceSpan(int start, int end, string replacement)
    {
        var text = GetPlainText();
        if (start < 0 || end < start || end > text.Length) return;
        SetPlainText(text[..start] + replacement + text[end..], start + replacement.Length);
    }

    private int GetCaretIndex() => Math.Clamp(_caret, 0, GetPlainText().Length);

    private void OnInnerPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Tab)
        {
            if (SuggestionsOpen && _items.Count > 0)
            {
                e.Handled = true;
                if (SelectedIndex >= 0 && SelectedIndex < _items.Count)
                    CommitSuggestion(_items[SelectedIndex]);
                else
                    CloseSuggestions();
                return;
            }
            if (!input.IsReadOnly && input.TextDocument is { } doc)
            {
                doc.Selection.TypeText("\t");
                e.Handled = true;
                return;
            }
        }
        PreviewKeyDown?.Invoke(this, e);
    }

    private void OnInnerTextChanged(object sender, RoutedEventArgs e)
    {
        if (_programmaticTextChange)
        {
            CloseSuggestions();
            return;
        }
        TextChanged?.Invoke(this, e);
        _ = UpdateSuggestionsAsync();
    }

    private void OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_programmaticTextChange || !SuggestionsOpen) return;
        _ = UpdateSuggestionsAsync();
    }

    private void OnSelectionChanging(object sender, RichEditBoxSelectionChangingEventArgs e)
    {
        if (_suppressArrowSelection)
        {
            _suppressArrowSelection = false;
            e.Cancel = true;
            return;
        }
        _caret = e.SelectionStart;
    }

    private async void OnPaste(object sender, TextControlPasteEventArgs e)
    {
        e.Handled = true;
        if (input is null) return;
        var content = Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.Text)) return;
        var text = await content.GetTextAsync();
        var caret = GetCaretIndex();
        ReplaceSpan(caret, caret, text);
    }

    private async Task UpdateSuggestionsAsync()
    {
        var seq = ++_suggestSeq;
        await Task.Delay(60);
        if (seq != _suggestSeq) return;

        var text = GetPlainText();
        var caret = GetCaretIndex();
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
            ShowSuggestionPanel(items);
        }
        else
        {
            CloseSuggestions();
        }
    }

    private void ShowSuggestionPanel(IReadOnlyList<SuggestionItem> items)
    {
        if (items.Count == 0 || input is null || suggestionsContainer is null)
        {
            CloseSuggestions();
            return;
        }
        _items.Clear();
        foreach (var item in items) _items.Add(item);
        SelectedIndex = 0;
        SuggestionHeight = Math.Min(280, items.Count * 31 + 8);
        var inputHeight = input.ActualHeight > 0 ? input.ActualHeight : 40;
        suggestionsContainer.Margin = new Thickness(0, -(inputHeight + SuggestionHeight + 4), 0, 0);
        SuggestionsOpen = true;
    }

    private void CloseSuggestions()
    {
        SuggestionsOpen = false;
        _items.Clear();
        SelectedIndex = -1;
    }

    private void OnProcessKeyboardAccelerators(object sender, ProcessKeyboardAcceleratorEventArgs e)
    {
        if (SuggestionsOpen && _items.Count > 0 && HandleSuggestionKey(e))
            return;

        if (e.Key != Windows.System.VirtualKey.Enter) return;
        if (InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            return;
        e.Handled = true;
        if (SubmitRequested is { } handler)
            _ = handler(this, GetPlainText());
    }

    private bool HandleSuggestionKey(ProcessKeyboardAcceleratorEventArgs e)
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
            _ = SetTextWithEnterSuppressionAsync("");
            input.Focus(FocusState.Programmatic);
            if (CommandTriggered is { } handler)
                _ = handler(this, item);
            return;
        }

        if (item.Kind == "file" && item.Detail != "directory")
        {
            var fileText = GetPlainText();
            var fileCaret = GetCaretIndex();
            if (fileText.Length > 0 && Controller.TryGetQuery(fileText, fileCaret, out _, out _, out var fileTokenStart))
                ReplaceSpan(fileTokenStart, fileCaret, item.Insert);
            CloseSuggestions();
            input.Focus(FocusState.Programmatic);
            if (FileCommitRequested is { } fileHandler)
                _ = fileHandler(this, item);
            return;
        }

        var text = GetPlainText();
        var caret = GetCaretIndex();
        if (text.Length == 0 || !Controller.TryGetQuery(text, caret, out _, out _, out var tokenStart)) return;

        ReplaceSpan(tokenStart, caret, item.Insert);
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
}
