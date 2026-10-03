using UnoVibe.Integration.Events;

namespace UnoVibe.Models;

/// <summary>
/// Atomic snapshot of a retry attempt. All retry-related fields change together,
/// so the whole object is replaced (same pattern as <see cref="SessionTokens"/>).
/// UI controls derive display strings from these raw values.
/// </summary>
class RetryState
{
    public required bool IsRetrying { get; init; }
    public required string Message { get; init; }
    public required int Attempt { get; init; }
    public required long NextMs { get; init; }

    public static RetryState None => new() { IsRetrying = false, Message = "", Attempt = 0, NextMs = 0 };

    public static RetryState From(SessionStatusRetry retry) => new()
    {
        IsRetrying = true,
        Message = retry.Message ?? "",
        Attempt = (int)retry.Attempt,
        NextMs = (long)retry.Next
    };
}
