namespace UnoVibe;

static class AppTheme
{
    private static Reference<Brush?>? _textOnAccent;
    public static Brush? TextOnAccent =>
        (_textOnAccent ??= ThemeResources.Get<Brush>("TextOnAccentFillColorPrimaryBrush", null)).Value;
}
