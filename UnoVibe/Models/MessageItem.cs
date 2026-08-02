namespace UnoVibe.Models;

public partial class MessageItem
{
    public string Id { get; set; } = "";
    public string Role { get; set; } = "";
    public string Agent { get; set; } = "";
    public ObservableCollection<PartItem> Parts { get; } = new();
}
