namespace UnoVibe.Controls;

/// <summary>
/// Produces suggestions for a single trigger character. Implementations decide their own filtering
/// (local contains-match, or a server round-trip) and the <see cref="SuggestionBoxController"/> only
/// merges results. Hosts of <see cref="SuggestBox"/> wire these up via
/// <see cref="SuggestBox.Providers"/>.
/// </summary>
public interface ISuggestionProvider
{
    /// <summary>Trigger character that activates this provider ('/' or '@').</summary>
    char Trigger { get; }

    /// <summary>Human-readable name for diagnostics.</summary>
    string Name { get; }

    /// <summary>Returns suggestions matching <paramref name="query"/>. An empty query means "show everything".</summary>
    Task<IReadOnlyList<SuggestionItem>> GetSuggestionsAsync(string query, CancellationToken ct = default);
}

/// <summary>
/// Parses the text before the caret for a suggestion trigger and dispatches queries to the
/// registered providers. Pure logic (no UI), so it is unit-testable and reusable.
///
/// The parsing rules:
///   - A trigger character ('/' for commands/skills, '@' for mentions/files) activates when it
///     begins a whitespace-delimited token — the start of the input, or preceded by whitespace —
///     so "foo /bar" and "foo @bar" trigger but "foo/bar" and "foo@bar" (e.g. email) do not.
///     Unlike opencode's TUI (which only accepts '/' as the very first character), any prefix works
///     mid-text here on purpose: <c>/skill-name</c> and <c>@filename</c> are meant to be insertable
///     inside a sentence.
///   - The query is everything between the trigger and the caret, with no whitespace allowed
///     (whitespace ends the token).
///   - Items marked <see cref="SuggestionItem.InputStartOnly"/> (built-in whole-input commands like
///     <c>/new</c>) are filtered out when the trigger token isn't at position 0, so they only ever
///     appear when the input starts with the trigger. Skills (<c>/quickmarkup</c>) and mentions
///     (<c>@path</c>) set it false and work anywhere.
/// </summary>
public sealed class SuggestionBoxController
{
    private readonly IReadOnlyList<ISuggestionProvider> _providers;
    private readonly string _prefixes;

    public SuggestionBoxController(IEnumerable<ISuggestionProvider> providers, string prefixes = "/@")
    {
        _providers = providers.ToArray();
        _prefixes = prefixes;
    }

    /// <summary>Trigger characters this controller reacts to.</summary>
    public string Prefixes => _prefixes;

    /// <summary>
    /// Finds the suggestion token ending at <paramref name="caret"/> in <paramref name="text"/>.
    /// Returns false when no trigger is active so the caller can dismiss the flyout.
    /// On success, <paramref name="tokenStart"/> is the index of the trigger character and
    /// <paramref name="query"/> is the text between the trigger and the caret.
    /// </summary>
    public bool TryGetQuery(string text, int caret, out char trigger, out string query, out int tokenStart)
    {
        trigger = '\0';
        query = "";
        tokenStart = 0;
        if (string.IsNullOrEmpty(text) || caret <= 0 || caret > text.Length) return false;

        // Walk back to the start of the current whitespace-delimited token.
        var start = caret - 1;
        while (start >= 0 && !char.IsWhiteSpace(text[start])) start--;
        start++;

        var token = text.Substring(start, caret - start);
        if (token.Length == 0) return false;

        var prefix = token[0];
        if (_prefixes.IndexOf(prefix) < 0) return false;

        // Every trigger must begin a token (start of input or preceded by whitespace), so
        // "foo/bar" and "foo@bar" are not treated as triggers, but "foo /bar" and "foo @bar" are.
        if (start > 0 && !char.IsWhiteSpace(text[start - 1])) return false;

        trigger = prefix;
        tokenStart = start;
        query = token.Substring(1);
        return true;
    }

    /// <summary>
    /// Merges (deduped by key) results from every provider that handles <paramref name="trigger"/>.
    /// When <paramref name="atInputStart"/> is false, items marked <see cref="SuggestionItem.InputStartOnly"/>
    /// (e.g. built-in whole-input commands) are dropped so they only ever appear at position 0.
    /// </summary>
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
