using Microsoft.UI.Dispatching;
using UnoVibe.Models;
using UnoVibe.Services;

namespace UnoVibe.Controls;

/// <summary>
/// App settings panel, rendered as a modal overlay over the main page. Rows are generated from
/// <see cref="SettingsStore.Specs"/>, so a new setting (a new spec + a GetValue/SetValue case)
/// appears here automatically. Every change is applied to the shared <see cref="SettingsStore"/>
/// immediately (persisted + propagated to every window and process); the panel re-reads the store
/// on <see cref="SettingsStore.Changed"/> so multiple open windows stay in sync.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Services;
    using UnoVibe.Models;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Microsoft.UI;
    inject bool SettingsOpen;
    public `ObservableCollection<SettingsEntry>` Entries = `new()`;
    <setup>
        var theme = ThemeBrushes.Global;
        var transparent = new SolidColorBrush(Colors.Transparent);
    </setup>
    <root>
        <Grid RowDefinitions=<>
            <RowDefinition Height=Auto />
            <RowDefinition />
        </>>
            <Grid Padding=`new Thickness(20, 16, 20, 12)` ColumnDefinitions=<>
                <ColumnDefinition Width=Auto />
                <ColumnDefinition />
            </> ColumnSpacing=12>
                <Button Background=`transparent` BorderThickness=0 Padding=`new Thickness(8, 4, 8, 4)` CornerRadius=6 @Click+=`SettingsOpen = false` ToolTipService.ToolTip="Back">
                    <AppSymbolIcon Symbol=Back FontSize=14 />
                </Button>
                <TextBlock Grid.Column=1 Text="Settings" FontSize=16 FontWeight=`FontWeights.SemiBold` VerticalAlignment=Center />
            </Grid>
            <ScrollViewer Grid.Row=1 VerticalScrollBarVisibility=Auto>
                <StackPanel MaxWidth=560 Padding=`new Thickness(20, 4, 20, 24)` Spacing=12>
                    foreach (var entry in `Entries`)
                    {
                        <Border Background=`theme.CardBackground` BorderBrush=`theme.CardStroke` BorderThickness=1 CornerRadius=8 Padding=`new Thickness(16, 12, 16, 12)`>
                            <StackPanel Spacing=8>
                                <TextBlock Text=`entry.Label` FontSize=13 FontWeight=`FontWeights.SemiBold` />
                                if (`entry.Description.Length > 0`)
                                    <TextBlock Text=`entry.Description` FontSize=11 Foreground=`theme.SecondaryText` TextWrapping=Wrap />
                                if (`entry.Kind == "text"`)
                                    <TextBox Text=`entry.Value` Text+=>`txt => OnEntryChanged(entry, txt ?? "")` PlaceholderText=`entry.Placeholder` MaxWidth=360 HorizontalAlignment=Left />
                                else if (`entry.Kind == "choice"`)
                                    <ComboBox ItemsSource=`entry.Options` ItemTemplate=template (SettingOption? opt) { <TextBlock Text=`opt?.Label ?? ""` /> } SelectedItem=`SelectedOption(entry)` SelectedItem+=>`sel => OnEntryChanged(entry, (sel as SettingOption)?.Value ?? "")` MinWidth=240 HorizontalAlignment=Left />
                                else if (`entry.Kind == "toggle"`)
                                    <ToggleSwitch IsOn=`entry.Value == "true"` IsOn+=>`on => OnEntryChanged(entry, on ? "true" : "false")` />
                            </StackPanel>
                        </Border>
                    }
                </StackPanel>
            </ScrollViewer>
        </Grid>
    </root>
    """)]
public partial class SettingsPage : IQuickMarkupComponent<Grid>
{
    private DispatcherQueue? _dispatcher;

    [QuickMarkupConstructor]
    private void Ctor()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        foreach (var spec in SettingsStore.Specs)
        {
            Entries.Add(new SettingsEntry
            {
                Key = spec.Key,
                Label = spec.Label,
                Description = spec.Description,
                Kind = spec.Kind,
                Value = SettingsStore.GetValue(spec.Key),
                Placeholder = spec.Placeholder ?? "",
                Options = new ObservableCollection<SettingOption>(spec.Options ?? Array.Empty<SettingOption>()),
            });
        }

        Init();
        SettingsStore.Changed += OnSettingsChanged;
    }

    /// <summary>The option matching the entry's current value (ComboBox SelectedItem), or null.</summary>
    private static SettingOption? SelectedOption(SettingsEntry entry) =>
        entry.Options.FirstOrDefault(o => o.Value == entry.Value);

    /// <summary>Applies a control's new value to the shared store (persisted + cross-window/proc).
    /// The entry's own <see cref="SettingsEntry.Value"/> is left to the store resync
    /// (<c>Changed</c> → <see cref="Resync"/>) so an in-place control edit (typing caret, combo
    /// selection, toggle state) is never clobbered by a re-render of the one-way binding.</summary>
    private void OnEntryChanged(SettingsEntry entry, string value)
    {
        SettingsStore.SetValue(entry.Key, value);
    }

    /// <summary>
    /// Re-reads the shared store after a change anywhere (this window, another window, or another
    /// process via the file watcher). Store changes may arrive on a background thread, so bounce
    /// to the UI thread first.
    /// </summary>
    private void OnSettingsChanged()
    {
        _ = _dispatcher?.TryEnqueue(Resync);
    }

    private void Resync()
    {
        foreach (var entry in Entries)
            entry.Value = SettingsStore.GetValue(entry.Key);
    }
}
