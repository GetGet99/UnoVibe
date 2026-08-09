using System.IO;
using System.Text.Json;
using UnoVibe.Models;

namespace UnoVibe.Services;

/// <summary>
/// Persists the ConnectPage "Recent" list (folders launched via `opencode serve`
/// and server URLs connected to) plus the global folder-security settings to a
/// small JSON file under the app's local-data directory. The folder-security
/// settings (`UseGeneratedPassword`/`CustomPassword`) are the single source of
/// truth for opening folders — both from history and from the Open Folder button.
/// Call <see cref="Load"/> once at startup; every mutation saves back automatically.
/// </summary>
public static class RecentConnectionsStore
{
    private const int MaxEntries = 20;

    private static readonly string Dir = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
    private static readonly string FilePath = Path.Combine(Dir, "recent.json");
    private static readonly object Gate = new();

    /// <summary>The recent connections, most recent first. Reactive in the markup.</summary>
    public static ObservableCollection<RecentConnection> Items { get; } = new();

    /// <summary>Global folder security: generate a strong password vs use <see cref="CustomPassword"/>.</summary>
    public static bool UseGeneratedPassword { get; set; } = true;

    /// <summary>
    /// Whether the custom folder password is persisted (opt-in, with a plain-text-risk warning in the UI).
    /// When false, <see cref="CustomPassword"/> is never written to disk.
    /// </summary>
    public static bool SaveFolderPassword { get; set; } = false;

    /// <summary>Global folder security: the custom password, only persisted when <see cref="SaveFolderPassword"/> is true.</summary>
    public static string CustomPassword { get; set; } = "";

    public static void Load()
    {
        try
        {
            lock (Gate)
            {
                if (!File.Exists(FilePath)) return;
                var json = File.ReadAllText(FilePath);

                List<RecentConnection>? list = null;
                try
                {
                    var file = JsonSerializer.Deserialize(json, AppJsonContext.Default.FileModel);
                    if (file is not null)
                    {
                        UseGeneratedPassword = file.UseGeneratedPassword;
                        SaveFolderPassword = file.SaveFolderPassword;
                        CustomPassword = file.CustomPassword;
                        list = file.Items;
                    }
                }
                catch (JsonException)
                {
                    // Legacy bare-array format — migrate on next save.
                    try { list = JsonSerializer.Deserialize(json, AppJsonContext.Default.ListRecentConnection); }
                    catch (JsonException) { list = null; }
                }

                if (list is null) return;

                // Legacy migration: recent.json files written before server passwords stopped
                // being persisted carried a raw `serverPassword` per server entry. Detect those
                // and mark the entries RequiresPassword so reopening prompts for the password.
                var legacyPasswordKeys = CollectLegacyPasswordKeys(json);

                Items.Clear();
                foreach (var item in list)
                {
                    if (item is null || string.IsNullOrWhiteSpace(item.Key)) continue;
                    if (!item.IsFolder && legacyPasswordKeys.Contains(item.Key))
                        item.RequiresPassword = true;
                    Items.Add(item);
                }
            }
        }
        catch
        {
            // Best effort: a corrupt/missing file just yields an empty history.
        }
    }

    /// <summary>Persists the global folder-security settings (the ConnectPage source of truth).
    /// The raw <paramref name="customPassword"/> is only written when <paramref name="savePassword"/> is true.</summary>
    public static void SaveSecurity(bool useGenerated, bool savePassword, string customPassword)
    {
        UseGeneratedPassword = useGenerated;
        SaveFolderPassword = savePassword;
        CustomPassword = savePassword ? customPassword : "";
        Save();
    }

    /// <summary>Records a successful local-folder launch (or refreshes an existing entry).</summary>
    public static void UpsertFolder(string folder)
    {
        var key = NormalizeFolder(folder);
        var item = Items.FirstOrDefault(x => x.IsFolder && x.Key == key);
        if (item is null)
        {
            item = new RecentConnection
            {
                Kind = RecentConnection.FolderKind,
                Key = key,
                Display = DisplayName(key),
                Detail = key,
            };
            Items.Insert(0, item);
        }
        else
        {
            Items.Move(Items.IndexOf(item), 0);
        }

        item.LastOpenedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        TrimAndSave();
    }

    /// <summary>Records a successful server connection (or refreshes an existing entry).
    /// The password itself is never persisted — only the fact that the server needs one,
    /// so a later click can prompt for it instead of connecting without auth.</summary>
    public static void UpsertServer(string url, bool requiresPassword)
    {
        var key = NormalizeUrl(url);
        var item = Items.FirstOrDefault(x => !x.IsFolder && x.Key == key);
        if (item is null)
        {
            item = new RecentConnection
            {
                Kind = RecentConnection.ServerKind,
                Key = key,
                Display = key,
                Detail = key,
            };
            Items.Insert(0, item);
        }
        else
        {
            Items.Move(Items.IndexOf(item), 0);
        }

        item.RequiresPassword = requiresPassword;
        item.LastOpenedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        TrimAndSave();
    }

    /// <summary>Removes one entry (by its normalized <see cref="RecentConnection.Key"/>).</summary>
    public static void Remove(string key)
    {
        var item = Items.FirstOrDefault(x => x.Key == key);
        if (item is null) return;
        Items.Remove(item);
        Save();
    }

    public static void ClearAll()
    {
        Items.Clear();
        Save();
    }

    private static void TrimAndSave()
    {
        while (Items.Count > MaxEntries) Items.RemoveAt(Items.Count - 1);
        Save();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            lock (Gate)
            {
                var file = new FileModel
                {
                    UseGeneratedPassword = UseGeneratedPassword,
                    CustomPassword = CustomPassword,
                    Items = Items.ToList(),
                };
                File.WriteAllText(FilePath, JsonSerializer.Serialize(file, AppJsonContext.Default.FileModel));
            }
        }
        catch
        {
            // Best effort: history persistence must never break the connect flow.
        }
    }

    private static string NormalizeFolder(string folder)
    {
        var path = folder.Trim();
        while (path.Length > 1 && (path.EndsWith('/') || path.EndsWith('\\'))) path = path[..^1];
        return path;
    }

    private static string NormalizeUrl(string url) => url.Trim().TrimEnd('/');

    /// <summary>
    /// Scans a persisted <c>recent.json</c> for server entries that stored a raw
    /// <c>serverPassword</c> (the pre-flag format) and returns their normalized keys.
    /// </summary>
    private static HashSet<string> CollectLegacyPasswordKeys(string json)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var root = JsonSerializer.Deserialize(json, AppJsonContext.Default.JsonElement);
            if (root.ValueKind == JsonValueKind.Array)
            {
                // Legacy bare-array format — every element is a connection.
                foreach (var el in root.EnumerateArray()) ScanLegacyPassword(el, keys);
            }
            else if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("items", out var items)
                && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in items.EnumerateArray()) ScanLegacyPassword(el, keys);
            }
        }
        catch
        {
            // Best effort; the typed deserialize above already succeeded, so this is only
            // a fallback for the old `serverPassword` key.
        }
        return keys;
    }

    private static void ScanLegacyPassword(JsonElement el, HashSet<string> keys)
    {
        if (el.ValueKind != JsonValueKind.Object) return;
        if (!el.TryGetProperty("kind", out var kind) || kind.GetString() != RecentConnection.ServerKind) return;
        if (!el.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String) return;
        if (!el.TryGetProperty("serverPassword", out var pw) || pw.ValueKind != JsonValueKind.String) return;
        if (string.IsNullOrEmpty(pw.GetString())) return;
        keys.Add(NormalizeUrl(key.GetString() ?? ""));
    }

    private static string DisplayName(string path)
    {
        var trimmed = path;
        var slash = trimmed.LastIndexOfAny(new[] { '/', '\\' });
        return slash >= 0 && slash < trimmed.Length - 1 ? trimmed[(slash + 1)..] : trimmed;
    }

    /// <summary>On-disk shape: the recent list plus the global folder-security settings.</summary>
    internal sealed class FileModel
    {
        public bool UseGeneratedPassword { get; set; } = true;
        public bool SaveFolderPassword { get; set; } = false;
        public string CustomPassword { get; set; } = "";
        public List<RecentConnection>? Items { get; set; }
    }
}
