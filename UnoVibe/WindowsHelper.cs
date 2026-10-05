namespace UnoVibe;

static class WindowsHelper
{
    public static void InitializeWithWindow(object target, Window window)
    {
#if WASDK
        WinRT.Interop.InitializeWithWindow.Initialize(target, (nint)window.AppWindow.Id.Value);
#endif
    }

    public static async Task<string?> PickFolderAsync(Window window, string? startPath)
    {
#if WASDK
        var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(window.AppWindow.Id);
        if (startPath is not null)
            picker.SuggestedStartFolder = startPath;
#elif DESKTOP_LINUX || (DESKTOP_MACOS && false)
        var picker = new FolderPicker(window);
        if (startPath is not null)
            picker.SuggestedStartFolder = startPath;
#else
        var picker = new Windows.Storage.Pickers.FolderPicker();
        InitializeWithWindow(picker, window);
#endif
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}