namespace UnoVibe.Pages.Main;

/// <summary>
/// Root page: session sidebar on the left, chat page on the right.
/// Also hosts the top-right toast overlay (from <c>tui.toast.show</c> events).
/// </summary>
[QuickMarkup("""
    using UnoVibe.Pages.Chat;
    using QuickMarkup.WinUI;
    using Microsoft.UI;
    provide NotificationProvider Notifications = `null!`;
    provide SessionsStateProvider Sessions = `null!`;
    provide EventsProvider Events = `null!`;
    provide ToastsProvider Toasts = `null!`;
    provide OpencodeConnection Connection = `null!`;
    provide OpencodeClient Opencode = `null!`;
    provide ModelsProvider Models = `null!`;
    provide Window HostWindow = `null!`;
    provide bool SettingsOpen = false;
    provide bool IsCompact = false;
    provide bool IsSidebarView = false;
    provide UIServiceProvider UIs = `new()`;
    <setup>
        Connection = providers.Connection;
        Opencode = providers.Connection.Client;
        HostWindow = hostWindow;
        Notifications = providers.Notifications;
        Events = providers.Events;
        Toasts = providers.Toasts;
        Sessions = providers.Sessions;
        Models = providers.Models;
    </setup>
    <Page SizeChanged+=`OnRootSizeChanged`>
        <MainContent />
    </Page>
    """)]
public partial class MainPage : IQuickMarkupComponent<Page>
{
    /// <summary>Viewport width (in pixels) below which the root switches to the compact small-screen layout.</summary>
    private const double CompactBreakpoint = 820;

    [QuickMarkupConstructor]
    private void Ctor(UnoVibeProviders providers, Window hostWindow) => Init(providers, hostWindow);

    /// <summary>Re-fits the root for the current window width: on small screens the sidebar and
    /// chat become a single full-width view (flag <c>IsSidebarView</c> picks which), while wide
    /// windows show both side by side. Entering/leaving compact resets the view flag to chat so a
    /// later resize starts from the session the user was reading.</summary>
    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < CompactBreakpoint;
        if (compact != IsCompact)
        {
            IsCompact = compact;
            IsSidebarView = false;
        }
    }
}
