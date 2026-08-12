namespace UnoVibe;

static class WindowsHelper
{
    public static void InitializeWithWindow(object target, Window window)
    {
#if WINDOWS
        WinRT.Interop.InitializeWithWindow.Initialize(target, (nint)window.AppWindow.Id.Value);
#endif
    }
}