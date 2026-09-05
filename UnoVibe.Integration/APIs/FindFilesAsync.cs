namespace UnoVibe.Integration;

/// <summary>
/// One entry from <c>GET /api/fs/find</c> or <c>GET /api/fs/list</c>.
/// </summary>
public sealed class FileSystemEntry
{
    public string Path { get; set; } = "";
    public string Type { get; set; } = "";
}

partial class OpencodeClient
{
    /// <summary>
    /// Get /api/fs/find?query=... — fuzzy file search. Returns <see cref="FileSystemEntry"/>
    /// objects with <c>Path</c> and <c>Type</c>. The server pre-filters and pre-ranks results.
    /// </summary>
    public async Task<Result<List<FileSystemEntry>>> FindFilesAsync(string query, string? directory = null,
        string? type = null, int limit = 20, CancellationToken ct = default)
    {
        var url = LocationUrl("/api/fs/find", directory);
        var separator = url.Contains('?') ? '&' : '?';
        url += $"{separator}query={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrEmpty(type)) url += $"&type={Uri.EscapeDataString(type)}";
        url += $"&limit={limit}";

        return await GetResultAsync(url, AppJsonContext.Default.ListFileSystemEntry, ct);
    }
}
