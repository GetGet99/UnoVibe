namespace UnoVibe.Models;

public sealed record SettingOption(string Value, string Label);

[QuickRefs("""
    public string Key = "";
    public string Label = "";
    public string Description = "";
    public string Kind = "";
    public string Value = "";
    public string Placeholder = "";
    public `ObservableCollection<SettingOption>` Options = `new()`;
    """)]
partial class SettingsEntry;
