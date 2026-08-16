namespace UnoVibe;

static class WindowsHelper
{
    public static void InitializeWithWindow(object target, Window window)
    {
#if WASDK
        WinRT.Interop.InitializeWithWindow.Initialize(target, (nint)window.AppWindow.Id.Value);
#endif
    }

    /// <summary>
    /// Opens the platform folder picker and returns the picked folder's path, or null when
    /// cancelled. On the WinAppSDK target the Windows App SDK <c>FolderPicker</c> is used so the
    /// dialog can open at an exact <paramref name="startPath"/> (the current window path, as shown
    /// in the window title); on other targets the classic <c>FolderPicker</c> is used, where the
    /// start path is not controlled.
    /// </summary>
    public static async Task<string?> PickFolderAsync(Window window, string startPath)
    {
#if WASDK
        var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(window.AppWindow.Id);
        if (startPath.Length > 0)
            picker.SuggestedStartFolder = startPath;
        var result = await picker.PickSingleFolderAsync();
        return result?.Path;
#else
        var picker = new Windows.Storage.Pickers.FolderPicker();
        InitializeWithWindow(picker, window);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
#endif
    }
}