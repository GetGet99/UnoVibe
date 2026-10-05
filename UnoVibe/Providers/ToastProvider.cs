using UnoVibe.Integration.Events;

namespace UnoVibe.Providers;

[QuickRefs("""
    ToastItem? CurrentToast;
    """)]
partial class ToastsProvider : IDisposable
{
    EventsProvider Event { get; set; }
    DispatcherQueue dispatcher;
    public ToastsProvider(EventsProvider Event, DispatcherQueue dispatcher)
    {
        this.Event = Event;
        this.dispatcher = dispatcher;
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

    public void DismissToast()
    {
        _toastCts?.Cancel();
        _toastCts = null;
        CurrentToast = null;
    }

    public void ShowError(Integration.ApiError message, string title = "Error")
        => ShowError(message.DisplayMessage, title);

    public void ShowWarning(Integration.ApiError message, string title = "Warning")
        => ShowWarning(message.DisplayMessage, title);

    public void ShowError(string message, string title = "Error")
        => Show(new ToastItem { Title = title, Message = message, Variant = "error", DurationMs = 8000 });

    public void ShowWarning(string message, string title = "Warning")
        => Show(new ToastItem { Title = title, Message = message, Variant = "warning", DurationMs = 6000 });

    public void Dispose()
    {
        _toastCts?.Cancel();
        _toastCts = null;
        GC.SuppressFinalize(this);
    }
}