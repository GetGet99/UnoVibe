using System.IO;
using System.Text.Json;
using UnoVibe.Models;

namespace UnoVibe.Services;

/// <summary>
/// Persists the ConnectPage "Recent" list (folders launched via `opencode serve`
/// and server URLs connected to) to a small JSON file under the user's app-data
/// directory. Each folder entry remembers its password settings so re-opening a
/// recent folder uses the same security configuration. Call <see cref="Load"/> once
/// at startup; every mutation saves back automatically.
/// </summary>
public static class RecentConnectionsStore
{
    private const int MaxEntries = 20;

    private static readonly string Dir = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
    private static readonly string FilePath = Path.Combine(Dir, "recent.json");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly object Gate = new();

    /// <summary>The recent connections, most recent first. Reactive in the markup.</summary>
    public static ObservableCollection<RecentConnection> Items { get; } = new();

    public static void Load()
    {
        try
        {
            lock (Gate)
            {
                if (!File.Exists(FilePath)) return;
                var list = JsonSerializer.Deserialize<List<RecentConnection>>(File.ReadAllText(FilePath), Json);
                if (list is null) return;
                Items.Clear();
                foreach (var item in list)
                {
                    if (item is null || string.IsNullOrWhiteSpace(item.Key)) continue;
                    Items.Add(item);
                }
            }
        }
        catch
        {
            // Best effort: a corrupt/missing file just yields an empty history.
        }
    }

    /// <summary>Records a successful local-folder launch (or refreshes an existing entry).</summary>
    public static void UpsertFolder(string folder, bool useGeneratedPassword, string customPassword)
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

        item.UseGeneratedPassword = useGeneratedPassword;
        item.CustomPassword = customPassword;
        item.LastOpenedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        TrimAndSave();
    }

    /// <summary>Records a successful server connection (or refreshes an existing entry).</summary>
    public static void UpsertServer(string url, string password)
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

        item.ServerPassword = password;
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
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Items.ToList(), Json));
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

    private static string DisplayName(string path)
    {
        var trimmed = path;
        var slash = trimmed.LastIndexOfAny(new[] { '/', '\\' });
        return slash >= 0 && slash < trimmed.Length - 1 ? trimmed[(slash + 1)..] : trimmed;
    }
}
