
using System.Text.Json;

namespace UnoVibe.Services;

internal static class OpencodeClientExtensions
{
    public static string GetStringProperty(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var prop) ? prop.GetString() ?? "" : "";

    public static long GetInt64Property(this JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop)) return 0;
        if (prop.ValueKind == JsonValueKind.Number) return prop.GetInt64();
        return 0;
    }

    public static bool GetBoolProperty(this JsonElement element, string name, bool fallback = false)
    {
        if (!element.TryGetProperty(name, out var prop)) return fallback;
        if (prop.ValueKind == JsonValueKind.True) return true;
        if (prop.ValueKind == JsonValueKind.False) return false;
        if (prop.ValueKind == JsonValueKind.String && prop.GetString() == "true") return true;
        return fallback;
    }
}
