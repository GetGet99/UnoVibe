using Microsoft.UI;

namespace UnoVibe.Pages.Chat;

[QuickMarkup("""
    using UnoVibe.Controls;
    using QuickMarkup.Infra.Collections;
    using Microsoft.UI;
    using Microsoft.UI.Xaml.Controls.Primitives;
    private Model? SelectedModel => `Sessions.ActiveChatParams.Model`;
    private string SelectedModelNameOrDefaultHint => `
        SelectedModel is not {} model
        ? "Select model"
        : (Models.ModelOptions.TryGetValue(model, out var modelOption)
            ? modelOption.Name
            : model.Formatted
        )`;
    public double FontSize = 12;
    inject SessionsStateProvider Sessions;
    inject OpencodeClient Opencode;
    inject ToastsProvider Toasts;
    inject ModelsProvider Models;
    inject bool IsCompact;
    string Query = "";
    int HighlightIndex = -1;
    `IEnumerable<ModelOption>` FilteredModels => `FilterModels(Models.ModelOptions, Query)`;
    string EmptyHint => `Models.ModelOptions.Count == 0 ? "No models available" : (Query.Trim().Length > 0 && !FilteredModels.Any() ? $"No models match \"{Query.Trim()}\"" : "")`;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        <Grid MinWidth=`IsCompact ? 120 : 200` MaxWidth=300 Height=28>
            triggerButton = <Button HorizontalAlignment=Stretch VerticalAlignment=Stretch Padding=`new Thickness(12,  0, 12,  0)` CornerRadius=4
                    HorizontalContentAlignment=Stretch VerticalContentAlignment=Center
                    ToolTipService.ToolTip=`SelectedModelNameOrDefaultHint`
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
                        <Border BorderBrush=`theme.DividerStroke` BorderThickness=`new Thickness(0, 1, 0, 0)`>
                            <Button HorizontalAlignment=Stretch HorizontalContentAlignment=Stretch
                                    CornerRadius=6 @Click+=`ConnectProvider()`>
                                <Grid ColumnSpacing=8 ColumnDefinitions=<><ColumnDefinition Width=Auto /><ColumnDefinition /></>>
                                    <AppSymbolIcon Symbol=Add FontSize=12 Foreground=`theme.Accent` VerticalAlignment=Center />
                                    <TextBlock Grid.Column=1 Text="Connect a provider…" FontSize=`FontSize` Foreground=`theme.Accent`
                                               TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center HorizontalAlignment=Left />
                                </Grid>
                            </Button>
                        </Border>
                    </StackPanel>
                </Border>
            </Flyout>>
                <Grid ColumnDefinitions=<>
                    <ColumnDefinition />
                    <ColumnDefinition Width=Auto />
                </>>
                    <TextBlock Text=`SelectedModelNameOrDefaultHint` FontSize=`FontSize` TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                    <FontIcon Glyph=`((char)0xE70D).ToString()` FontSize=12 Grid.Column=1 VerticalAlignment=Center Margin=`new Thickness(12, 0, 2, 0)` Foreground=`theme.SecondaryText` />
                </Grid>
            </Button>
        </Grid>
    </root>
    """)]
partial class ModelPicker : IQuickMarkupComponent<Grid>
{
    private const double RowHeight = 34;

    void OnModelSelected(ModelOption model)
    {
        var charparams = Sessions.ActiveChatParams;

        if (model == Sessions.Head(Sessions.ActiveSessionId)?.ChatParams.Model) return;
        charparams.Model = Model.From(model);
        if (!model.Variants.Contains(charparams.Variant))
            charparams.Variant = null;
    }

    private bool _suppressArrowSelection;

    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();
        modelFlyout.FlyoutPresenterStyle = new()
        {
            BasedOn = (Style)
#if WASDK
            Application.Current.Resources["DefaultFlyoutPresenterStyle"]
#else
            Application.Current.Resources["DefaultFlyoutPresenter"]
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

    private static IEnumerable<ModelOption> FilterModels(IEnumerable<ModelOption> source, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return source;
        var q = query.Trim();
        return source.Where(m =>
            m.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            m.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            m.ProviderId.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    private void SelectModel(ModelOption m)
    {
        if (modelFlyout is { IsOpen: true }) modelFlyout.Hide();
        OnModelSelected(m);
    }

    private void ConnectProvider()
    {
        if (modelFlyout is { IsOpen: true }) modelFlyout.Hide();
        _ = OpenProviderDialogAsync();
    }

    private async Task OpenProviderDialogAsync()
    {
        var xamlRoot = MarkupNode.XamlRoot;
        if (xamlRoot is null) return;
        await ProviderConnectDialog.ShowAsync(Opencode, Toasts, Models, xamlRoot);
    }

    private bool IsSelected(ModelOption m) => SelectedModel == m;

    private Brush? RowBackground(ModelOption m, int index) =>
        IsSelected(m)
            ? SelectedRowBackground
            : index == HighlightIndex ? ThemeBrushes.Global.SubtleFill : new SolidColorBrush(Colors.Transparent);

    private static Brush? SelectedRowBackground =>
        ThemeBrushes.Global.Accent is SolidColorBrush accent
            ? new SolidColorBrush(accent.Color) { Opacity = 0.18 }
            : ThemeBrushes.Global.CardBackground;

    public void Open()
    {
        if (modelFlyout is { IsOpen: false } && triggerButton is not null)
            modelFlyout.ShowAt(triggerButton);
    }

    private void OnFlyoutOpened(object? sender, object e)
    {
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
        if (SelectedModel is not { } sel) return -1;
        var idx = 0;
        foreach (var m in FilteredModels)
        {
            if (m == sel) return idx;
            idx++;
        }
        return -1;
    }

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

    private void OnQueryChanged(object sender, TextChangedEventArgs e)
    {
        HighlightIndex = 0;
        if (listScroll is not null) listScroll.ChangeView(null, 0, null, true);
    }
}
