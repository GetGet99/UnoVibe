namespace UnoVibe.Integration;

partial class OpencodeClient
{
    /// <summary>
    /// Get /find/file?query=... — fuzzy file search. Returns file paths as strings.
    /// The server pre-filters and pre-ranks results.
    /// </summary>
    public async Task<Result<List<string>>> FindFilesAsync(string query, string? directory = null,
        string? type = null, int limit = 20, CancellationToken ct = default)
    {
        var url = LocationUrl("/api/fs/find", directory);
        var separator = url.Contains('?') ? '&' : '?';
        url += $"{separator}query={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrEmpty(type)) url += $"&type={Uri.EscapeDataString(type)}";
        url += $"&limit={limit}";

        return await GetResultAsync(url, AppJsonContext.Default.ListString, ct);
    }
}
