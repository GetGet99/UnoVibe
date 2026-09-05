namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>
    /// Get /agent — lists all agents
    /// </summary>
    public Task<Result<List<AgentInfo>>> GetModesAsync(CancellationToken ct = default)
        => GetResultAsync("/agent", AppJsonContext.Default.ListAgentInfo, ct);
}
