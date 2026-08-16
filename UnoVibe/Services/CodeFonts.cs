using Microsoft.UI.Xaml.Media;

namespace UnoVibe.Services;

/// <summary>
/// Resolves the monospaced "code" font — code blocks, inline code, diffs, tool output, the
/// suggestion list, permission bodies — from the <see cref="SettingsStore.CodeFont"/> setting.
///
/// The empty setting (the default) maps to a monospaced font that actually ships with each OS:
/// <c>Consolas</c> on Windows, <c>DejaVu Sans Mono</c> on Linux (the <c>monospace</c> fontconfig
/// default on nearly every distro), and <c>Menlo</c> on macOS. <c>Consolas</c> does not exist on
/// Linux/macOS, so the old single hardcoded family silently fell back to the app's default sans
/// font on those platforms (no monospace rendering); these names are present out of the box.
/// Any other value is used verbatim (installed custom fonts work).
/// </summary>
public static class CodeFonts
{
    /// <summary>Stored value for "use the per-platform default".</summary>
    public const string DefaultValue = "";

    private static readonly Dictionary<string, FontFamily> Cache = new();

    /// <summary>The <see cref="FontFamily"/> for the current setting, resolved live so a setting
    /// change takes effect on the next render.</summary>
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