// using UnoVibe.Integration;

namespace UnoVibe.Services;

// [QuickMarkup("""
//     bool IsRead;
//     SessionState State => `ResolveState()`;
//     private SessionState ChatState; // None, Sucesss, Error, Interrupted
//     private bool PendingQuestion;
//     private bool PendingPermission;
//     """)]
// public class SessionHead
// {
//     public string Id { get; private set; }
//     [QuickMarkupConstructor]
//     void Ctor(string id, OpencodeClient client)
//     {
//         Id = id;
//     }
    // SessionState ResolveState()
    // {
    //     if (PendingPermission) return SessionState.PendingPermission;
    //     if (PendingQuestion) return SessionState.PendingPermission;
    //     if (IsRead) return SessionState.None;
    //     return ChatState;
    // }
// }
// /*
//     Id
//     IsRead / IsUnread
//     IsBusy
//     NeedsAttention
//     Outcome
//     Title
//     TimeLabel
// */
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
public enum ChatOutcome
{
    None,
    Success,
    Error,
    Interrupted
}