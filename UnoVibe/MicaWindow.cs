using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace UnoVibe;

/// <summary>
/// A <see cref="Window"/> that hosts its content on a single root <see cref="Grid"/>.
/// When the platform supports it, a <see cref="MicaBackdrop"/> is applied behind the
/// content; otherwise the root grid falls back to the theme <c>SolidBackground</c> brush
/// (kept in sync with theme changes). Assign content via <see cref="Child"/>.
/// </summary>
public class MicaWindow : Window
{
    private readonly Grid _root = new();

    public MicaWindow()
    {
        Content = _root;
        ApplyBackground();
    }


    /// <summary>
    /// The window's content. Replaces any previously assigned child.
    /// </summary>
    public UIElement? Child
    {
        get => _root.Children.Count > 0 ? _root.Children[0] : null;
        set
        {
            _root.Children.Clear();
            if (value is not null) _root.Children.Add(value);
        }
    }

    private void ApplyBackground()
    {
#if WASDK
        AppWindow.TitleBar.PreferredTheme = Microsoft.UI.Windowing.TitleBarTheme.UseDefaultAppMode;
#endif
        if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
        {
            try
            {
                // DataTemplateDelegator
                SystemBackdrop = new MicaBackdrop();
                return; // keep the root transparent so Mica shows through
            }
            catch (Exception ex)
            {
                // Runtime without a SystemBackdrop implementation — fall back to solid.
                System.Diagnostics.Debug.WriteLine($"MicaWindow: Mica not available ({ex.Message})");
            }
        }

        // Fallback: paint the theme solid background, re-applied on theme changes.
        ThemeBrushes.Global.SolidBackgroundProp.Watch(brush => _root.Background = brush, true);
    }
}
