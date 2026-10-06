using System.Diagnostics.CodeAnalysis;
using UnoVibe.Models.Startup;
using UnoVibe.Pages.Connect;
using UnoVibe.Pages.Main;
using UnoVibe.Pages.Test;

namespace UnoVibe;

sealed class WindowController
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

    public void ShowTest()
    {
        Window.Child = new TestPage().MarkupNode;
        Window.Title = "UnoVibe - Test";
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