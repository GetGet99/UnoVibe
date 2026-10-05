using Windows.UI;
using Windows.UI.ViewManagement;

namespace UnoVibe.Helpers;

static class AccentPaletteHelper
{
    public const double HueShiftDegrees = -40;

    private static readonly UISettings Ui = new();

    public static Color PrimaryAccent(ThemeBrushes theme) =>
        theme.Accent is SolidColorBrush { Color: var color } ? color : Color.FromArgb(0, 0, 0, 0);

    public static Color SecondaryAccent(Color primary) => HueShift(primary, HueShiftDegrees);

    public static Color HueShift(Color color, double degrees)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        if (delta == 0) return color;

        double h = max == r ? ((g - b) / delta % 6)
            : max == g ? (b - r) / delta + 2
            : (r - g) / delta + 4;
        h = (h * 60 + degrees) % 360;
        if (h < 0) h += 360;

        double s = delta / max;
        int sextant = (int)(h / 60) % 6;
        double f = h / 60 - Math.Floor(h / 60);
        double p = max * (1 - s);
        double q = max * (1 - f * s);
        double t = max * (1 - (1 - f) * s);
        (double cr, double ca, double cb) = sextant switch
        {
            0 => (max, t, p),
            1 => (q, max, p),
            2 => (p, max, t),
            3 => (p, q, max),
            4 => (t, p, max),
            _ => (max, p, q),
        };
        return Color.FromArgb(color.A, (byte)Math.Round(cr * 255), (byte)Math.Round(ca * 255), (byte)Math.Round(cb * 255));
    }

    public static (Color Light3, Color Light2, Color Light1, Color Base, Color Dark1, Color Dark2, Color Dark3) GetColorPalette(Color c) =>
        (ChangeColorBrightness(c, 0.51),
         ChangeColorBrightness(c, 0.25),
         ChangeColorBrightness(c, 0.02),
         c,
         ChangeColorBrightness(c, -0.19),
         ChangeColorBrightness(c, -0.40),
         ChangeColorBrightness(c, -0.68));

    public static Color ChangeColorBrightness(Color color, double correctionFactor)
    {
        double red = color.R;
        double green = color.G;
        double blue = color.B;

        if (correctionFactor < 0)
        {
            correctionFactor = 1 + correctionFactor;
            red *= correctionFactor;
            green *= correctionFactor;
            blue *= correctionFactor;
        }
        else
        {
            red = (255 - red) * correctionFactor + red;
            green = (255 - green) * correctionFactor + green;
            blue = (255 - blue) * correctionFactor + blue;
        }

        return Color.FromArgb(color.A, (byte)red, (byte)green, (byte)blue);
    }

    public static Brush? InlineCodeBrush(ThemeBrushes theme)
    {
        var primary = PrimaryAccent(theme);
        if (primary.A == 0) return null;

        var palette = GetColorPalette(SecondaryAccent(primary));
        var color = Ui.GetColorValue(UIColorType.Background).R < 255 / 2 ? palette.Light2 : palette.Dark2;
        return new SolidColorBrush(color);
    }
}