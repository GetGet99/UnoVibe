namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>
    /// Post /session/{id}/abort — interrupts the currently-running turn and stops all
    /// ongoing AI processing / command execution for the session. Safe to call anytime.
    /// </summary>
    public Task AbortAsync(string sessionId, CancellationToken ct = default)
        => PostAsync($"/session/{sessionId}/abort", ct);
}
