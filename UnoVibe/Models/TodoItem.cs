namespace UnoVibe.Models;

sealed class TodoItem
{
    public string Status { get; set; } = "";
    public string Content { get; set; } = "";
    public string Priority { get; set; } = "";
}
