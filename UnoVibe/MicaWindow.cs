namespace UnoVibe;

class MicaWindow : Window
{
    private readonly Grid _root = new();

    public MicaWindow()
    {
        Content = _root;
        ApplyBackground();
    }

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
                SystemBackdrop = new MicaBackdrop();
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MicaWindow: Mica not available ({ex.Message})");
            }
        }

        ThemeBrushes.Global.SolidBackgroundProp.Watch(brush => _root.Background = brush, true);
    }
}
