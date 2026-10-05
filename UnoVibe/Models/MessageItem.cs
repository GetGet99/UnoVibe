namespace UnoVibe.Models;

partial class MessageItem
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
    public bool Interrupted { get; set; }
    public ObservableCollection<ChatPartItem> Parts { get; } = new();
}
