namespace UnoVibe.Helpers;

static class SystemFontsHelper
{
    private static string[]? _families;

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
                    _families = Array.Empty<string>();
                }
            }
            return _families;
        }
    }

    private static IEnumerable<string> Enumerate()
    {
#if WASDK
        return Microsoft.Graphics.Canvas.Text.CanvasTextFormat.GetSystemFontFamilies();
#else
        return SkiaSharp.SKFontManager.Default.FontFamilies;
#endif
    }
}