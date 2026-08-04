namespace UnoVibe.Models;

/// <summary>
/// A transient toast notification to surface in the UI, from the server's
/// <c>tui.toast.show</c> event. <see cref="Variant"/> is one of
/// "info"|"success"|"warning"|"error" and drives the accent/background colors.
/// </summary>
[QuickMarkup("""
    public string Title = "";
    public string Message = "";
    public string Variant = "info";
    """)]
public partial class ToastItem
{
    /// <summary>How long to show the toast, in milliseconds. 0/negative = persistent.</summary>
    public int DurationMs { get; set; } = 5000;
}