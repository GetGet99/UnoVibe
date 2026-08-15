using QuickMarkup.WinUI;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using UnoVibe.Models;

namespace UnoVibe.Pages.Chat;

/// <summary>
/// Searchable model picker for the chat toolbar. A <see cref="DropDownButton"/> that keeps the
/// closed-state look of the Mode/Variant combos next to it (same chevron glyph, height, padding)
/// but, instead of a plain dropdown, opens a flyout with a filter box above the model list —
/// because <c>ComboBox.IsTextSearchEnabled</c> is not implemented on Uno.
///
/// UX:
///   - Closed: a combo-like button showing the selected model's name (ellipsis-trimmed).
///   - Open: the search box is focused (caret at end, query preserved across opens); the list is
///     filtered as you type on name/id/provider; the currently-selected model is pre-highlighted
///     and scrolled into view; arrow keys move the highlight, Enter picks it, Escape dismisses.
///   - Rows show the model name + provider; the active model gets an accent tint + a check glyph.
///
/// API: bind <see cref="ItemsSource"/> (the full model list) and <see cref="SelectedItem"/>
/// (one-way display/state), then handle <see cref="ModelSelected"/> to apply the pick.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Models;
    using UnoVibe.Controls;
    using QuickMarkup.Infra.Collections;
    using Microsoft.UI;
    using Microsoft.UI.Xaml.Controls.Primitives;
    public `ObservableCollection<ModelOption>` ItemsSource = `new()`;
    public ModelOption? SelectedItem = null;
    public double FontSize = 12;
    inject? bool IsCompact;
    string Query = "";
    int HighlightIndex = -1;
    // Filtered model list — reactive to both the source collection and the query string.
    `IEnumerable<ModelOption>` FilteredModels => `FilterModels(ItemsSource.Reactive, Query)`;
    bool EmptyModels => `ItemsSource.Reactive.Count == 0`;
    // Hint shown when there is nothing to pick ("No models available" / "No models match ...").
    string EmptyHint => `EmptyModels ? "No models available" : (Query.Trim().Length > 0 && !FilteredModels.Any() ? $"No models match \"{Query.Trim()}\"" : "")`;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        <Grid MinWidth=`IsCompact ? 120 : 200` MaxWidth=300 Height=28>
            triggerButton = <Button HorizontalAlignment=Stretch VerticalAlignment=Stretch Padding=`new Thickness(12,  0, 12,  0)` CornerRadius=4
                    HorizontalContentAlignment=Stretch VerticalContentAlignment=Center
                    ToolTipService.ToolTip=`SelectedItem?.Name ?? "Select model"`
                    Flyout=modelFlyout = <Flyout Placement=Bottom Opened+=`OnFlyoutOpened` Closed+=`OnFlyoutClosed`>
                <Border MinWidth=340 MaxWidth=460 Padding=8 CornerRadius=8>
                    <StackPanel Spacing=6>
                        <Grid>
                            searchBox = <TextBox Text<=>`Query` PlaceholderText="Search models..." Height=32 FontSize=`FontSize`
                                    VerticalContentAlignment=Center
                                    Padding=`new Thickness(28, 4, 8, 4)` PreviewKeyDown+=`OnSearchKeyDown`
                                    TextChanged+=`OnQueryChanged` />
                            <AppSymbolIcon Symbol=Find FontSize=12 Foreground=`theme.TertiaryText`
                                    HorizontalAlignment=Left VerticalAlignment=Center Margin=`new Thickness(8, 0, 0, 0)` IsHitTestVisible=false />
                        </Grid>
                        listScroll = <ScrollViewer MaxHeight=320 VerticalScrollBarVisibility=Auto HorizontalContentAlignment=Stretch>
                            <StackPanel HorizontalAlignment=Stretch>
                                foreach (index; var m in `FilteredModels`; `$"{m.ProviderId}/{m.Id}"`)
                                {
                                    <Button Height=34 HorizontalAlignment=Stretch Padding=`new Thickness(10,  0, 10,  0)` HorizontalContentAlignment=Stretch
                                            Background=`RowBackground(m, index)` BorderThickness=0 CornerRadius=6
                                            ToolTipService.ToolTip=`m.Name`
                                            @Click+=`SelectModel(m)`>
                                        <Grid ColumnSpacing=8 ColumnDefinitions=<>
                                            <ColumnDefinition />
                                            <ColumnDefinition Width=Auto />
                                            <ColumnDefinition Width=Auto />
                                        </>>
                                            <TextBlock Grid.Column=0 Text=`m.Name` FontSize=`FontSize` TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                                            <TextBlock Grid.Column=1 Text=`m.ProviderId` FontSize=`FontSize - 1` Foreground=`theme.SecondaryText` MaxWidth=90 TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                                            <Grid Grid.Column=2 HorizontalAlignment=Center VerticalAlignment=Center>
                                                <AppSymbolIcon Symbol=Accept FontSize=`FontSize` Foreground=`theme.Accent`
                                                        Visibility=`IsSelected(m) ? Visibility.Visible : Visibility.Collapsed` />
                                            </Grid>
                                        </Grid>
                                    </Button>
                                }
                                if (`EmptyHint.Length > 0`)
                                    <Border Padding=`new Thickness(10,  14, 10,  14)` HorizontalAlignment=Stretch>
                                        <TextBlock Text=`EmptyHint` FontSize=12 Foreground=`theme.SecondaryText` TextWrapping=Wrap HorizontalAlignment=Center />
                                    </Border>
                            </StackPanel>
                        </ScrollViewer>
                    </StackPanel>
                </Border>
            </Flyout>>
                <Grid ColumnDefinitions=<>
                    <ColumnDefinition />
                    <ColumnDefinition Width=Auto />
                </>>
                    <TextBlock Text=`SelectedItem?.Name ?? "Select model"` FontSize=`FontSize` TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                    <FontIcon Glyph=`((char)0xE70D).ToString()` FontSize=12 Grid.Column=1 VerticalAlignment=Center Margin=`new Thickness(12, 0, 2, 0)` Foreground=`theme.SecondaryText` />
                </Grid>
            </Button>
        </Grid>
    </root>
    """)]
public partial class ModelPicker : IQuickMarkupComponent<Grid>
{
    /// <summary>Fixed row height in the model list; the scroll-to-selected math relies on it.</summary>
    private const double RowHeight = 34;

    /// <summary>Raised when the user picks a model from the list. The subscriber applies it (SessionStore.SetModel).</summary>
    public event Action<ModelOption>? ModelSelected;

    // Uno workaround state (same issue as SuggestBox): a handled Up/Down in the search TextBox still
    // moves the caret via Uno's unconditional OnPostKeyDown processing; cancelling the stray move in
    // SelectionChanging keeps the caret put while the list highlight moves.
    private bool _suppressArrowSelection;

    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();
        modelFlyout.FlyoutPresenterStyle = new()
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
        searchBox.SelectionChanging += OnSelectionChanging;
    }

    // ── Filtering ────────────────────────────────────────────────────────────────

    private static IEnumerable<ModelOption> FilterModels(ObservableCollection<ModelOption> source, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return source;
        var q = query.Trim();
        return source.Where(m =>
            m.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            m.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            m.ProviderId.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    // ── Selection / visual state ─────────────────────────────────────────────────

    private void SelectModel(ModelOption m)
    {
        if (modelFlyout is { IsOpen: true }) modelFlyout.Hide();
        ModelSelected?.Invoke(m);
    }

    private bool IsSelected(ModelOption m) =>
        SelectedItem is { } s && s.ProviderId == m.ProviderId && s.Id == m.Id;

    private Brush? RowBackground(ModelOption m, int index) =>
        IsSelected(m)
            ? SelectedRowBackground
            : index == HighlightIndex ? ThemeBrushes.Global.SubtleFill : new SolidColorBrush(Colors.Transparent);

    /// <summary>Low-alpha accent tint marking the currently-selected model row.</summary>
    private static Brush? SelectedRowBackground =>
        ThemeBrushes.Global.Accent is SolidColorBrush accent
            ? new SolidColorBrush(accent.Color) { Opacity = 0.18 }
            : ThemeBrushes.Global.CardBackground;

    // ── Flyout lifecycle ─────────────────────────────────────────────────────────

    private void OnFlyoutOpened(object? sender, object e)
    {
        // Focus the search box with the caret at the end (preserves the query across opens),
        // pre-highlight the currently selected model so arrow keys start from it, then scroll it
        // into view once the flyout's popup has laid out.
        _ = FocusSearchAsync();
        HighlightIndex = IndexOfSelected();
        _ = listScroll?.DispatcherQueue?.TryEnqueue(ScrollHighlightIntoView);
    }

    private void OnFlyoutClosed(object? sender, object e) => HighlightIndex = -1;

    private async Task FocusSearchAsync()
    {
        await Task.Yield();
        if (searchBox is null) return;
        searchBox.Focus(FocusState.Programmatic);
        searchBox.SelectionStart = searchBox.Text.Length;
    }

    private int IndexOfSelected()
    {
        if (SelectedItem is not { } sel) return -1;
        var idx = 0;
        foreach (var m in FilteredModels)
        {
            if (m.ProviderId == sel.ProviderId && m.Id == sel.Id) return idx;
            idx++;
        }
        return -1;
    }

    // ── Keyboard navigation ──────────────────────────────────────────────────────

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Up:
                e.Handled = true;
                _suppressArrowSelection = true;
                _ = searchBox?.DispatcherQueue?.TryEnqueue(() => _suppressArrowSelection = false);
                MoveHighlight(-1);
                break;
            case Windows.System.VirtualKey.Down:
                e.Handled = true;
                _suppressArrowSelection = true;
                _ = searchBox?.DispatcherQueue?.TryEnqueue(() => _suppressArrowSelection = false);
                MoveHighlight(1);
                break;
            case Windows.System.VirtualKey.Enter:
                e.Handled = true;
                CommitHighlight();
                break;
            case Windows.System.VirtualKey.Escape:
                e.Handled = true;
                if (modelFlyout is { IsOpen: true }) modelFlyout.Hide();
                break;
        }
    }

    /// <summary>
    /// Uno bug workaround (see field docs): cancels the stray caret move a handled Up/Down still
    /// triggers in the single-line search box.
    /// </summary>
    private void OnSelectionChanging(TextBox sender, TextBoxSelectionChangingEventArgs e)
    {
        if (_suppressArrowSelection)
        {
            _suppressArrowSelection = false;
            e.Cancel = true;
        }
    }

    private void MoveHighlight(int delta)
    {
        var count = CountFiltered();
        if (count == 0) return;
        HighlightIndex = HighlightIndex < 0
            ? (delta > 0 ? 0 : count - 1)
            : (HighlightIndex + delta + count) % count;
        ScrollHighlightIntoView();
    }

    private int CountFiltered() => FilteredModels.Count();

    private void CommitHighlight()
    {
        if (HighlightIndex < 0) return;
        var m = FilteredModels.ElementAtOrDefault(HighlightIndex);
        if (m is not null) SelectModel(m);
    }

    private void ScrollHighlightIntoView()
    {
        if (listScroll is null || HighlightIndex < 0) return;
        var rowTop = HighlightIndex * RowHeight;
        var target = Math.Clamp(rowTop - (listScroll.ViewportHeight - RowHeight) / 2,
            0, Math.Max(0, listScroll.ScrollableHeight));
        listScroll.ChangeView(null, target, null, true);
    }

    // ── Search box helpers ───────────────────────────────────────────────────────

    private void OnQueryChanged(object sender, TextChangedEventArgs e)
    {
        HighlightIndex = 0;
        if (listScroll is not null) listScroll.ChangeView(null, 0, null, true);
    }
}
