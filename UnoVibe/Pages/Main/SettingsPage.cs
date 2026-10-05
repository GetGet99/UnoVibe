namespace UnoVibe.Pages.Main;

[QuickMarkup("""
    using UnoVibe.Controls;
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
partial class SettingsPage : IQuickMarkupComponent<Grid>
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

    private static SettingOption? SelectedOption(SettingsEntry entry) =>
        entry.Options.FirstOrDefault(o => o.Value == entry.Value);

    private void OnEntryChanged(SettingsEntry entry, string value)
    {
        SettingsStore.SetValue(entry.Key, value);
    }

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
