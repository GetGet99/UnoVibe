using Microsoft.UI.Xaml.Media;
using QuickMarkup.Infra;
using QuickMarkup.WinUI;

namespace UnoVibe;

public static class AppTheme
{
    // Contrast text for Accent fills (black on light accent, white on dark accent).
    // ThemeBrushes doesn't expose this one.
    private static Reference<Brush?>? _textOnAccent;
    public static Brush? TextOnAccent =>
        (_textOnAccent ??= ThemeResources.Get<Brush>("TextOnAccentFillColorPrimaryBrush", null)).Value;
}
