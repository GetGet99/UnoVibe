namespace UnoVibe.Providers;

public class NotificationProvider(Window HostWindow)
{
    public void NotifyCompleted(SessionHead? session, ChatOutcome outcome, bool visibleWhenFocused)
        => NotificationsHelper.NotifyCompleted(HostWindow, session, outcome, visibleWhenFocused);
}