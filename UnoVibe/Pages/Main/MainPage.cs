namespace UnoVibe.Pages.Main;

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
partial class MainPage : IQuickMarkupComponent<Page>
{
    private const double CompactBreakpoint = 820;

    [QuickMarkupConstructor]
    private void Ctor(UnoVibeProviders providers, Window hostWindow) => Init(providers, hostWindow);

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
