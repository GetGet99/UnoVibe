using System.Diagnostics.CodeAnalysis;
using UnoVibe.Models.Startup;
using UnoVibe.Pages.Connect;
using UnoVibe.Pages.Main;

namespace UnoVibe;

/// <summary>
/// Tracks one top-level <see cref="MicaWindow"/> together with its own
/// <see cref="ChatStoreToBeRemoved"/>. This lets each window scope to an independent
/// (or shared) opencode serve session instead of a single global store.
/// </summary>
public sealed class WindowController
{
    public event Action? Disposed;

    [NotNull]
    public MicaWindow? Window {
        get => field ?? throw new ObjectDisposedException("WindowController");
        private set;
    } = new();

    public UnoVibeProviders? Providers { get; private set; }

    public WindowController()
    {
        Window.SetWindowIcon();
        Window.Closed += OnClose;
    }

    void OnClose(object sender, WindowEventArgs args)
    {
        Providers?.Dispose();
        Providers = null;
        Window = null;
        Disposed?.Invoke();
    }

    public void ShowConnect(StartupArgs? startup = null)
    {
        Window.Child = new ConnectPage(this, startup).MarkupNode;
        Window.Title = "UnoVibe - Welcome";
    }

    public void ShowMain(OpencodeConnection connection)
    {
        Providers?.Dispose();
        Providers = new(connection, Window);
        var label = connection.DisplayLabel;
        Window.Child = new MainPage(Providers, Window).MarkupNode;
        Window.Title = string.IsNullOrEmpty(label) ? "UnoVibe" : $"UnoVibe - {label}";
    }
}