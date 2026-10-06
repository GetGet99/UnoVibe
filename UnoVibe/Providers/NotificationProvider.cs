namespace UnoVibe.Providers;

class NotificationProvider(Window HostWindow)
{
    public void NotifyCompleted(SessionHead? session, ChatOutcome outcome, bool visibleWhenFocused)
        => NotificationsHelper.NotifyCompleted(HostWindow, session, outcome, visibleWhenFocused);
    public void NotifyPermission(SessionHead? session, string permissionTitle, string body, bool visibleWhenFocused)
        => NotificationsHelper.NotifyPermission(HostWindow, session, permissionTitle, body, visibleWhenFocused);
    public void NotifyQuestion(SessionHead? session, string question, bool visibleWhenFocused)
        => NotificationsHelper.NotifyQuestion(HostWindow, session, question, visibleWhenFocused);
}