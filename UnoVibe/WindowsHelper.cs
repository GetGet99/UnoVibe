namespace UnoVibe;

static class WindowsHelper
{
    public static void InitializeWithWindow(object target, Window window)
    {
#if WASDK
        WinRT.Interop.InitializeWithWindow.Initialize(target, (nint)window.AppWindow.Id.Value);
#endif
    }
}