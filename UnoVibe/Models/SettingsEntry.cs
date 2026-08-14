using System.Collections.ObjectModel;

namespace UnoVibe.Models;

/// <summary>One selectable option of a choice-type setting: a stored value plus a display label.</summary>
public sealed record SettingOption(string Value, string Label);

/// <summary>
/// Reactive row model for the settings page, built from <see cref="UnoVibe.Services.SettingsStore.Specs"/>.
/// The page renders each row's control from <see cref="Kind"/> and binds <see cref="Value"/> (the
/// UI-facing string form) back into the store via <c>SettingsStore.SetValue</c>.
/// </summary>
[QuickMarkup("""
    public string Key = "";
    public string Label = "";
    public string Description = "";
    public string Kind = "";
    public string Value = "";
    public string Placeholder = "";
    public `ObservableCollection<SettingOption>` Options = `new()`;
    """)]
public partial class SettingsEntry;
