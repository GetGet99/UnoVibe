namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>
    /// Get /agent — lists all agents
    /// </summary>
    public Task<Result<List<AgentInfo>>> GetAgentsAsync(CancellationToken ct = default)
        => GetResultAsync("/agent", AppJsonContext.Default.ListAgentInfo, ct);
}
