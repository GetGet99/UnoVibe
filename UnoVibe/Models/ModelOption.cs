namespace UnoVibe.Models;

public sealed class ModelOption
{
    public string ProviderId { get; set; } = "";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string[] Variants { get; set; } = [];
}
