using System.Text.Json;

namespace UnoVibe.Stores;

static class RecentConnectionsStore
{
    private const int MaxEntries = 20;

    private static readonly string Dir = ApplicationData.Current.LocalFolder.Path;
    private static readonly string FilePath = Path.Combine(Dir, "recent.json");
    private static readonly object Gate = new();

    public static ObservableCollection<RecentConnection> Items { get; } = new();

    public static bool UseGeneratedPassword { get; set; } = true;

    public static bool SaveFolderPassword { get; set; } = false;

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
                    try { list = JsonSerializer.Deserialize(json, AppJsonContext.Default.ListRecentConnection); }
                    catch (JsonException) { list = null; }
                }

                if (list is null) return;

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
        }
    }

    public static void SaveSecurity(bool useGenerated, bool savePassword, string customPassword)
    {
        UseGeneratedPassword = useGenerated;
        SaveFolderPassword = savePassword;
        CustomPassword = savePassword ? customPassword : "";
        Save();
    }

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
        }
    }

    private static string NormalizeFolder(string folder)
    {
        var path = folder.Trim();
        while (path.Length > 1 && (path.EndsWith('/') || path.EndsWith('\\'))) path = path[..^1];
        return path;
    }

    private static string NormalizeUrl(string url) => url.Trim().TrimEnd('/');

    private static HashSet<string> CollectLegacyPasswordKeys(string json)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var root = JsonSerializer.Deserialize(json, AppJsonContext.Default.JsonElement);
            if (root.ValueKind == JsonValueKind.Array)
            {
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

    internal sealed class FileModel
    {
        public bool UseGeneratedPassword { get; set; } = true;
        public bool SaveFolderPassword { get; set; } = false;
        public string CustomPassword { get; set; } = "";
        public List<RecentConnection>? Items { get; set; }
    }
}
