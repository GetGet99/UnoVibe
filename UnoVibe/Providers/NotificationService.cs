using UnoVibe.Models;
using UnoVibe.Services;

namespace UnoVibe.Providers;

class NotificationService(Window HostWindow)
{
    public void NotifyCompleted(SessionHead? session, ChatOutcome outcome, bool visibleWhenFocused)
        => NotificationsHelper.NotifyCompleted(HostWindow, session, outcome, visibleWhenFocused);
}