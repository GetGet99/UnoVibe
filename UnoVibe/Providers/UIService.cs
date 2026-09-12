
using UnoVibe.Models;

class UIService
{
    public event Action? McpSectionRequested;
    public void InvokeMcpSectionRequested() => McpSectionRequested?.Invoke();
    public event Action<SessionId>? ForkAndSwitchSessionRequested;
    public event Action<SessionId, MessageItem>? ForkAndSwitchSessionWithMessageRequested;
    public void ForkAndSwitchSession(SessionId session) => ForkAndSwitchSessionRequested?.Invoke(session);
    public void ForkAndSwitchSession(SessionId session, MessageItem message) => ForkAndSwitchSessionWithMessageRequested?.Invoke(session, message);
    public event Action? ScrollChatToBottomRequested;
    public void ScrollChatToBottom() => ScrollChatToBottomRequested?.Invoke();
    public event Action? BeginRenameAndFocusRequested;
    public void BeginRenameAndFocus() => BeginRenameAndFocusRequested?.Invoke();
}