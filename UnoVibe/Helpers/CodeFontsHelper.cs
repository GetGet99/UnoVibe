namespace UnoVibe.Helpers;

static class CodeFontsHelper
{
    public const string DefaultValue = "";

    private static readonly Dictionary<string, FontFamily> Cache = [];

    public static FontFamily Current
    {
        get
        {
            var name = ResolveName(SettingsStore.CodeFont);
            lock (Cache)
            {
                if (!Cache.TryGetValue(name, out var family))
                    Cache[name] = family = new FontFamily(name);
                return family;
            }
        }
    }

    private static string ResolveName(string setting)
    {
        if (!string.IsNullOrWhiteSpace(setting)) return setting;
#if WINDOWS
        return "Consolas";
#elif DESKTOP_LINUX
        return "DejaVu Sans Mono";
#elif DESKTOP_MACOS
        return "Menlo";
#else
        return "Consolas";
#endif
    }
}