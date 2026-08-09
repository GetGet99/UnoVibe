using Microsoft.UI.Xaml;
using UnoVibe.Pages;
using UnoVibe.Services;

namespace UnoVibe;

/// <summary>
/// Tracks one top-level <see cref="MicaWindow"/> together with its own
/// <see cref="ChatStore"/>. This lets each window scope to an independent
/// (or shared) opencode serve session instead of a single global store.
/// </summary>
public sealed class WindowController
{
    public MicaWindow Window { get; } = new();

    public ChatStore Store { get; } = new();

    public void ShowConnect(StartupArgs? startup = null)
    {
        Window.Child = new ConnectPage(x =>
        {
            x.Controller = this;
            x.Startup = startup;
        });
        Window.Title = "UnoVibe - Welcome";
    }

    public void ShowMain()
    {
        var label = Store.DisplayLabel;
        Window.Child = new MainPage(x => x.ProvideStore(Store));
        Window.Title = string.IsNullOrEmpty(label) ? "UnoVibe" : $"UnoVibe - {label}";
    }
}