namespace UnoVibe.Controls;

interface ISuggestionProvider
{
    char Trigger { get; }

    string Name { get; }

    Task<IReadOnlyList<SuggestionItem>> GetSuggestionsAsync(string query, CancellationToken ct = default);
}

sealed class SuggestionBoxController
{
    private readonly IReadOnlyList<ISuggestionProvider> _providers;
    private readonly string _prefixes;

    public SuggestionBoxController(IEnumerable<ISuggestionProvider> providers, string prefixes = "/@")
    {
        _providers = providers.ToArray();
        _prefixes = prefixes;
    }

    public string Prefixes => _prefixes;

    public bool TryGetQuery(string text, int caret, out char trigger, out string query, out int tokenStart)
    {
        trigger = '\0';
        query = "";
        tokenStart = 0;
        if (string.IsNullOrEmpty(text) || caret <= 0 || caret > text.Length) return false;

        var start = caret - 1;
        while (start >= 0 && !char.IsWhiteSpace(text[start])) start--;
        start++;

        var token = text.Substring(start, caret - start);
        if (token.Length == 0) return false;

        var prefix = token[0];
        if (_prefixes.IndexOf(prefix) < 0) return false;

        if (start > 0 && !char.IsWhiteSpace(text[start - 1])) return false;

        trigger = prefix;
        tokenStart = start;
        query = token.Substring(1);
        return true;
    }

    public async Task<IReadOnlyList<SuggestionItem>> GetSuggestionsAsync(char trigger, string query,
        bool atInputStart, CancellationToken ct = default)
    {
        var results = new List<SuggestionItem>();
        foreach (var provider in _providers)
        {
            if (provider.Trigger != trigger) continue;
            foreach (var item in await provider.GetSuggestionsAsync(query, ct))
            {
                if (item.InputStartOnly && !atInputStart) continue;
                if (results.All(r => r.Key != item.Key)) results.Add(item);
            }
        }
        return results;
    }
}
