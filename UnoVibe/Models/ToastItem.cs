namespace UnoVibe.Models;

[QuickRefs("""
    public string Title = "";
    public string Message = "";
    public string Variant = "info";
    """)]
partial class ToastItem
{
    public int DurationMs { get; set; } = 5000;
}