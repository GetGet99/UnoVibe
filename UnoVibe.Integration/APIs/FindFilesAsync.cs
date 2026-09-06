namespace UnoVibe.Integration;

/// <summary>
/// One entry from <c>GET /api/fs/find</c> or <c>GET /api/fs/list</c>.
/// </summary>
public sealed class FileSystemEntry
{
    public string Path { get; set; } = "";
    public string Type { get; set; } = "";
}

/// <summary>
/// The <c>project</c> object inside <see cref="LocationInfo"/>.
/// </summary>
public sealed class LocationProjectInfo
{
    public string Id { get; set; } = "";
    public string Directory { get; set; } = "";
}

/// <summary>
/// The <c>location</c> object returned by every v2 <c>/api/*</c> endpoint.
/// </summary>
public sealed class LocationInfo
{
    public string Directory { get; set; } = "";
    public string? WorkspaceID { get; set; }
    public LocationProjectInfo Project { get; set; } = new();
}

partial class OpencodeClient
{
    /// <summary>
    /// Get /api/fs/find?query=... — fuzzy file search. The server pre-filters and pre-ranks
    /// results (frecency, fuzzy score, filename bonus), so callers must NOT re-sort.
    /// </summary>
    public async Task<Result<APIEntryResponse<List<FileSystemEntry>>>> FindFilesAsync(string query, string? directory = null,
        string? type = null, int limit = 20, CancellationToken ct = default)
    {
        var url = LocationUrl("/api/fs/find", directory);
        var separator = url.Contains('?') ? '&' : '?';
        url += $"{separator}query={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrEmpty(type)) url += $"&type={Uri.EscapeDataString(type)}";
        url += $"&limit={limit}";

        return await GetResultAsync(url, AppJsonContext.Default.APIEntryResponseListFileSystemEntry, ct);
    }
}
