namespace UnoVibe.Helpers;

static class SessionUsageHelper
{
    public static (double Cost, SessionTokens Tokens, MessageItem? Last) Summarize(IEnumerable<MessageItem> messages)
    {
        double cost = 0;
        MessageItem? last = null;
        foreach (var message in messages)
        {
            if (message.Role != "assistant") continue;
            cost += message.Cost;
            if (message.TokensOutput > 0) last = message;
        }
        return (cost, last is null ? SessionTokens.Zero : SessionTokens.From(last), last);
    }

    public static long ResolveContextLimit(IEnumerable<ModelOption> options, MessageItem? message)
    {
        if (message is null) return 0;
        return options.FirstOrDefault(m => m.Id == message.ModelId
            && (message.ProviderId.Length == 0 || m.ProviderId == message.ProviderId))?.LimitContext ?? 0;
    }
}
