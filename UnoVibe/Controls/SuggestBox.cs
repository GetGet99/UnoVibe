using System.Collections.ObjectModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;

namespace UnoVibe.Controls;

/// <summary>
/// A plain-text suggestion box: a multiline <see cref="TextBox"/> that pops a suggestion flyout
/// when the caret is inside a trigger token ("/" at the start of the input, or any other configured
/// prefix such as "@" at the start of a token).
///
/// Self-contained and portable — to reuse in another QuickMarkup project, copy this file together
/// with <c>SuggestionItem.cs</c> and <c>SuggestionBoxController.cs</c> and implement
/// <see cref="ISuggestionProvider"/> for your data source. The control only depends on
/// QuickMarkup + the WinUI types in <see cref="GlobalUsings"/>.
///
/// API (a trimmed-down, plain-text take on CommunityToolkit's RichSuggestBox):
///   - <see cref="Prefixes"/> — trigger characters (default "/@").
///   - <see cref="Providers"/> — suggestion sources; the control parses the token, queries every
///     provider matching the trigger, and shows the merged results.
///   - <see cref="SubmitRequested"/> — raised on bare Enter (without Shift) while the flyout is
///     closed; Shift+Enter always inserts a newline. Hosts decide what to do (send the message) and
///     call <see cref="Clear"/> to reset the box.
///   - Properties/events not listed here (e.g. <c>Text</c>, <c>PlaceholderText</c>, <c>MaxHeight</c>,
///     <c>PreviewKeyDown</c>) are forwarded to the underlying <see cref="TextBox"/>.
/// </summary>
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
            <Border Background=`theme.CardBackground` BorderBrush=`theme.CardStroke` BorderThickness=`new Thickness(1)` CornerRadius=8 Padding=4 MinWidth=320 MaxWidth=480
                    GotFocus+=`OnSuggestionsGotFocus`>
                <ScrollViewer MaxHeight=280>
                    <StackPanel>
                        foreach (index; var item in `_items`; `item.Key`)
                        {
                            <Button Padding=`new Thickness(10, 6)` HorizontalContentAlignment=Left
                                    Background=`index == SelectedIndex ? theme.SubtleFill : Colors.Transparent`
                                    BorderThickness=0 CornerRadius=6
                                    @Click+=`await CommitSuggestionAsync(item)`>
                                <StackPanel Orientation=Horizontal Spacing=8>
                                    <Border Background=`KindBadgeBrush(item.Kind)` CornerRadius=3 Padding=`new Thickness(5, 1, 5, 2)` VerticalAlignment=Center>
                                        <TextBlock Text=`item.KindLabel` FontSize=10 Foreground=`AppTheme.TextOnAccent` FontWeight=`FontWeights.SemiBold` />
                                    </Border>
                                    <TextBlock Text=`item.Text` FontSize=12 FontFamily="Consolas" VerticalAlignment=Center />
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
public partial class SuggestBox : IQuickMarkupComponent<TextBox>
{
    /// <summary>Handler for <see cref="SubmitRequested"/>.</summary>
    public delegate Task SubmitHandler(SuggestBox sender, string text);

    /// <summary>
    /// Raised when the user presses Enter without Shift while the suggestion flyout is closed.
    /// The text at the time of the key press is passed as the second argument.
    /// </summary>
    public event SubmitHandler? SubmitRequested;

    private readonly ObservableCollection<SuggestionItem> _items = new();

    /// <summary>Stale-response guard for the async suggestion fetch (bumped on every text change).</summary>
    private int _suggestSeq;

    private IReadOnlyList<ISuggestionProvider>? _providers;
    private SuggestionBoxController? _controller;

    /// <summary>Suggestion sources for this box. Set before the user starts typing.</summary>
    public IReadOnlyList<ISuggestionProvider>? Providers
    {
        get => _providers;
        set
        {
            _providers = value;
            _controller = null; // rebuilt lazily so a late set also picks up the current Prefixes
        }
    }

    private SuggestionBoxController Controller =>
        _controller ??= new SuggestionBoxController(Providers ?? Array.Empty<ISuggestionProvider>(), Prefixes);

    /// <summary>
    /// Clears the input text. Includes an Uno workaround (briefly toggling <c>AcceptsReturn</c>) so a
    /// multiline TextBox actually repaints empty.
    /// </summary>
    public void Clear() => _ = ClearCoreAsync();

    private async Task ClearCoreAsync()
    {
        if (input is null) return;
        input.Text = "";
        input.AcceptsReturn = false;
        await Task.Delay(16);
        input.AcceptsReturn = true;
    }

    // ── Suggestion pipeline ───────────────────────────────────────────────────────

    /// <summary>Fired on every input change (typing, paste, programmatic edits). Re-parses the caret token.</summary>
    private void OnTextChanged(object sender, TextChangedEventArgs e) => _ = UpdateSuggestionsAsync();

    private async Task UpdateSuggestionsAsync()
    {
        var seq = ++_suggestSeq;
        await Task.Delay(60); // light debounce; network-backed providers add their own latency
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

    /// <summary>Resets selection state when the flyout dismisses without a commit (light dismiss / Escape).</summary>
    private void OnSuggestFlyoutClosed(object? sender, object e)
    {
        SelectedIndex = -1;
        _items.Clear();
    }

    /// <summary>
    /// Bounces focus straight back to the input whenever anything inside the suggestion flyout gets
    /// focus (e.g. a pointer click on a row), so typing keeps working. Mirrors RichSuggestBox's
    /// SuggestionList_GotFocus — combined with <c>ShowMode=Transient</c> (which never steals focus on
    /// open, unlike Standard-mode flyouts) the editor keeps focus for the whole suggestion session.
    /// </summary>
    private void OnSuggestionsGotFocus(object sender, RoutedEventArgs e) => input?.Focus(FocusState.Programmatic);

    /// <summary>
    /// Keyboard navigation for the suggestion flyout. Returns true when the key was consumed.
    /// Shift+Enter still inserts a newline; Enter/Tab without Shift commit the selection.
    /// </summary>
    private bool HandleSuggestionKey(KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Up:
                e.Handled = true;
                SelectedIndex = SelectedIndex <= 0 ? _items.Count - 1 : SelectedIndex - 1;
                return true;
            case Windows.System.VirtualKey.Down:
                e.Handled = true;
                SelectedIndex = SelectedIndex >= _items.Count - 1 ? 0 : SelectedIndex + 1;
                return true;
            case Windows.System.VirtualKey.Enter:
                if (InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
                    return false; // Shift+Enter = newline
                e.Handled = true;
                if (SelectedIndex >= 0 && SelectedIndex < _items.Count)
                    CommitSuggestion(_items[SelectedIndex]);
                else
                    CloseSuggestions();
                return true;
            case Windows.System.VirtualKey.Tab:
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

    /// <summary>Replaces the typed token with the suggestion's Insert text (keeps the rest of the input intact).</summary>
    private void CommitSuggestion(SuggestionItem item)
    {
        if (input is null) return;
        var text = input.Text;
        var caret = input.SelectionStart;
        if (!Controller.TryGetQuery(text, caret, out _, out _, out var tokenStart)) return;

        var newText = text.Remove(tokenStart, caret - tokenStart).Insert(tokenStart, item.Insert);
        input.Text = newText;
        input.SelectionStart = tokenStart + item.Insert.Length;
        CloseSuggestions();
        input.Focus(FocusState.Programmatic);
    }

    /// <summary>Pill color for a suggestion's kind badge: cmd = accent, skill = caution, file = success, agent = attention.</summary>
    private static Brush? KindBadgeBrush(string kind) => kind switch
    {
        "skill" => ThemeBrushes.Global.SystemCaution,
        "file" => ThemeBrushes.Global.SystemSuccess,
        "agent" => ThemeBrushes.Global.SystemAttention,
        _ => ThemeBrushes.Global.Accent,
    };

    // ── Keyboard handling ─────────────────────────────────────────────────────────

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (suggestFlyout is { IsOpen: true } && _items.Count > 0 && HandleSuggestionKey(e))
            return;

        if (e.Key != Windows.System.VirtualKey.Enter) return;
        if (InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            return;
        e.Handled = true;

        var text = input.Text;
        if (SubmitRequested is { } handler)
            _ = handler(this, text);
    }
}
