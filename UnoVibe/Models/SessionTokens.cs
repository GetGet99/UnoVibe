namespace UnoVibe.Models;

class SessionTokens
{
    public required long Input { get; init; }
    public required long Output { get; init; }
    public required long Reasoning { get; init; }
    public required long CacheRead { get; init; }
    public required long CacheWrite { get; init; }
    public long TokensTotal => Input + Output + Reasoning + CacheRead + CacheWrite;
    public static SessionTokens Zero => new() { CacheRead = 0, CacheWrite = 0, Input = 0, Output = 0, Reasoning = 0 };

    public static SessionTokens From(MessageItem message)
        => new()
        {
            Input = message.TokensInput,
            Output = message.TokensOutput,
            Reasoning = message.TokensReasoning,
            CacheRead = message.TokensCacheRead,
            CacheWrite = message.TokensCacheWrite
        };
}