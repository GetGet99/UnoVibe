using Microsoft.UI.Xaml;
using UnoVibe.Pages.Connect;
using UnoVibe.Pages.Main;
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

    public WindowController()
    {
        // Each window's store knows its own window so toast focus-gating is per-window.
        Store.OwnerWindow = Window;
    }

    public void ShowConnect(StartupArgs? startup = null)
    {
        Window.Child = new ConnectPage(this, startup).MarkupNode;
        Window.Title = "UnoVibe - Welcome";
    }

    public void ShowMain()
    {
        var label = Store.DisplayLabel;
        Window.Child = new MainPage(Store, Window).MarkupNode;
        Window.Title = string.IsNullOrEmpty(label) ? "UnoVibe" : $"UnoVibe - {label}";
    }
}