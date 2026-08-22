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
    /// cancelled. On every target the dialog can open at an exact <paramref name="startPath"/>
    /// (the current window path, as shown in the window title):
    /// the WinAppSDK target uses the Windows App SDK <c>FolderPicker</c>, and the Skia Linux/macOS
    /// targets use the WASDK-shaped <c>FolderPicker</c> polyfill (UnoVibe/Polyfills/*). Only the
    /// Skia Windows target still falls back to Uno's classic <c>FolderPicker</c>, where the start
    /// path is not controlled.
    /// </summary>
    public static async Task<string?> PickFolderAsync(Window window, string startPath)
    {
#if WASDK
        var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(window.AppWindow.Id);
        if (startPath.Length > 0)
            picker.SuggestedStartFolder = startPath;
#elif DESKTOP_LINUX || (DESKTOP_MACOS && false) // MacOS currently fails, so I'd like to do this instead.
        // `FolderPicker` / `PickFolderResult` resolve to the platform polyfill registered in
        // UnoVibe/Polyfills/Linux (or MacOS) — see AGENTS.md "Polyfills".
        var picker = new FolderPicker(window);
        if (startPath.Length > 0)
            picker.SuggestedStartFolder = startPath;
#else
        var picker = new Windows.Storage.Pickers.FolderPicker();
        InitializeWithWindow(picker, window);
#endif
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}