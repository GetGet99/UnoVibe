namespace UnoVibe.Providers;

public class UnoVibeProviders : IDisposable
{
    public OpencodeConnection Connection { get; }
    public SessionsStateProvider Sessions { get; }
    public NotificationProvider Notifications { get; }
    public EventsProvider Events { get; }
    public ToastsProvider Toasts { get; }
    public ModelsProvider Models { get; }
    public Window HostWindow { get; }
    IDisposable[] Disposables => [Connection, Events];

    public UnoVibeProviders(OpencodeConnection connection, Window hostWindow)
    {
        Connection = connection;
        HostWindow = hostWindow;
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        Notifications = new(hostWindow);
        Events = new EventsProvider(Connection.Client, dispatcher);
        Toasts = new(Events, dispatcher);
        Sessions = new(Connection, Events, Toasts, Notifications, dispatcher);
        Models = new() { Opencode = Connection.Client, Toasts = Toasts };
    }
    public void Dispose()
    {
        foreach (var disposable in Disposables)
            disposable.Dispose();
        GC.SuppressFinalize(this);
    }
}