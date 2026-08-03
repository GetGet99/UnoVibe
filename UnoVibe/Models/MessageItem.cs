namespace UnoVibe.Models;

public partial class MessageItem
{
    public string Id { get; set; } = "";
    public string Role { get; set; } = "";
    public string Agent { get; set; } = "";
    public string ModelId { get; set; } = "";
    public string ProviderId { get; set; } = "";
    public double Cost { get; set; }
    public long TokensInput { get; set; }
    public long TokensOutput { get; set; }
    public long TokensReasoning { get; set; }
    public long TokensCacheRead { get; set; }
    public long TokensCacheWrite { get; set; }
    public ObservableCollection<PartItem> Parts { get; } = new();
}
