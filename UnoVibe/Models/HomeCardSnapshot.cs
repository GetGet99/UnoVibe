namespace UnoVibe.Models;

[QuickRefs("""
    int TodoDone;
    int TodoTotal;
    PermissionRequestItem? ActivePermission;
    SessionTokens Tokens = `SessionTokens.Zero`;
    double Cost;
    long ContextLimit;
    bool IsLoading = true;
    `Dictionary<string, HashSet<string>>` PartFilter = `new()`;
    """)]
partial class HomeCardSnapshot
{
    public SessionId SessionId { get; }

    public ObservableCollection<MessageItem> PreviewMessages { get; } = new();

    public HomeCardSnapshot(SessionId sessionId)
    {
        SessionId = sessionId;
    }

    public bool HasPreview => PreviewMessages.Count > 0;

    public IReadOnlySet<string>? VisibleParts(string messageId) =>
        PartFilter.TryGetValue(messageId, out var parts) ? parts : null;
}
