using Microsoft.UI.Xaml;
using UnoVibe.Pages;
using UnoVibe.Services;

namespace UnoVibe;

/// <summary>
/// Tracks one top-level <see cref="Window"/> together with its own
/// <see cref="ChatStore"/>. This lets each window scope to an independent
/// (or shared) opencode serve session instead of a single global store.
/// </summary>
public sealed class WindowController
{
    public Window Window { get; } = new();

    public ChatStore Store { get; } = new();

    public void ShowConnect() => Window.Content = new ConnectPage(x => x.Controller = this);

    public void ShowMain() => Window.Content = new MainPage(x => x.ProvideStore(Store));
}