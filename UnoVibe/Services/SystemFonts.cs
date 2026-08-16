using System;
using System.Collections.Generic;
using System.Linq;

namespace UnoVibe.Services;

/// <summary>
/// Enumerates the installed system font families so the Code font setting can offer the user's own
/// fonts instead of just a curated list. Uses each platform's real font registry:
/// - Skia desktop (Linux/macOS/Windows via the Uno Skia host): <c>SkiaSharp.SKFontManager.Default.FontFamilies</c> —
///   the same font manager the renderer uses to resolve <c>FontFamily</c>, so every listed name matches.
/// - WASDK (native WinUI on Windows): Win2D's <c>CanvasTextFormat.GetSystemFontFamilies()</c>, which reads
///   DirectWrite's system font collection.
/// SkiaSharp is not referenced directly — it comes transitively from Uno's Skia host, so desktop targets
/// get it with no new dependency. The result is cached; failures yield an empty list (the setting then
/// shows only its Default option).
/// </summary>
public static class SystemFonts
{
    private static string[]? _families;

    /// <summary>All installed font families, sorted case-insensitively and deduplicated.</summary>
    public static IReadOnlyList<string> Families
    {
        get
        {
            if (_families is null)
            {
                try
                {
                    _families = Enumerate()
                        .Where(static n => !string.IsNullOrWhiteSpace(n))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(static n => n, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
                catch
                {
                    // Best effort: no font-registry access → the picker offers only "Default".
                    _families = Array.Empty<string>();
                }
            }
            return _families;
        }
    }

    private static IEnumerable<string> Enumerate()
    {
#if WASDK
        // Win2D wraps DirectWrite; this static reads the system font collection (WinUI text stack).
        return Microsoft.Graphics.Canvas.Text.CanvasTextFormat.GetSystemFontFamilies();
#else
        // The same font manager that resolves FontFamily in the Skia renderer (fontconfig on
        // Linux, CoreText on macOS, DirectWrite on Windows) — names round-trip via FontFamily.
        return SkiaSharp.SKFontManager.Default.FontFamilies;
#endif
    }
}