using System.Diagnostics.CodeAnalysis;
using UnoVibe.Integration.Events;
using UnoVibe.Models;

namespace UnoVibe.Providers;

[QuickMarkup("""
    using UnoVibe.Models;
    ToastItem? CurrentToast;
    """)]
public partial class ToastService
{
    EventSource Event { get; set; }
    DispatcherQueue dispatcher;
    [QuickMarkupConstructor]
    [MemberNotNull(nameof(Event), nameof(dispatcher))]
    void Ctor(EventSource Event, DispatcherQueue dispatcher)
    {
        this.Event = Event;
        this.dispatcher = dispatcher;
        Init(Event, dispatcher);
        Event.RegisterTuiToastShow(null, ApplyToastShow);
    }

    private void ApplyToastShow(string _, TuiToastShowEvent e)
    {
        Show(new ToastItem
        {
            Title = e.Title ?? "Opencode TUI Message",
            Message = e.Message,
            Variant = e.Variant,
            DurationMs = (int) e.Duration,
        });
    }
    private CancellationTokenSource? _toastCts;

    /// <summary>Shows a toast, replacing any current one, and auto-dismisses it after <see cref="ToastItem.DurationMs"/>.</summary>
    public void Show(ToastItem toast)
    {
        _toastCts?.Cancel();
        _toastCts = null;
        CurrentToast = toast;
        if (toast.DurationMs <= 0) return;

        var cts = new CancellationTokenSource();
        _toastCts = cts;
        _ = DismissToastAfterAsync(toast.DurationMs, cts.Token);
    }

    private async Task DismissToastAfterAsync(int durationMs, CancellationToken ct)
    {
        try
        {
            await Task.Delay(durationMs, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        dispatcher.TryEnqueue(() =>
        {
            if (ct.IsCancellationRequested) return;
            CurrentToast = null;
        });
    }

    /// <summary>Immediately hides the current toast (clear any pending auto-dismiss).</summary>
    public void DismissToast()
    {
        _toastCts?.Cancel();
        _toastCts = null;
        CurrentToast = null;
    }


    /// <summary>
    /// Shows an error toast. The one sanctioned way to surface a failure to the user —
    /// <see cref="ConnectionStatus"/> is reserved for the connect lifecycle ("Connecting...",
    /// "Connected") because the sidebar footer renders it in an unreadably small strip
    /// (see AGENTS.md "Contribution rules and banned patterns").
    /// </summary>
    public void ShowError(Integration.ApiError message, string title = "Error")
        => ShowError(message.DisplayMessage, title);

    /// <summary>
    /// Shows an error toast. The one sanctioned way to surface a failure to the user —
    /// <see cref="ConnectionStatus"/> is reserved for the connect lifecycle ("Connecting...",
    /// "Connected") because the sidebar footer renders it in an unreadably small strip
    /// (see AGENTS.md "Contribution rules and banned patterns").
    /// </summary>
    public void ShowError(string message, string title = "Error")
        => Show(new ToastItem { Title = title, Message = message, Variant = "error", DurationMs = 8000 });

    /// <summary>Shows a warning toast for transient notices that are not outright failures
    /// (e.g. a stale permission/question card that was answered elsewhere).</summary>
    public void ShowWarning(string message, string title = "Warning")
        => Show(new ToastItem { Title = title, Message = message, Variant = "warning", DurationMs = 6000 });
    
    public void Dispose()
    {
        _toastCts?.Cancel();
        _toastCts = null;
    }
}