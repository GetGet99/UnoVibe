namespace UnoVibe.Models;

public enum SessionState
{
    None,
    Working,
    PendingQuestion,
    PendingPermission,
    Success,
    Error,
    Interrupted
}
