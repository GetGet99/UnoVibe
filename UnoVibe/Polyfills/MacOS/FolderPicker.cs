#if DESKTOP_MACOS
// Registers the macOS polyfill `FolderPicker` as the app-wide `FolderPicker` type, so
// `WindowsHelper.PickFolderAsync` (and any other caller) can use the WASDK-shaped API without
// platform #if. (`PickFolderResult` is registered in PickFolderResult.cs.) See AGENTS.md Polyfills.
global using FolderPicker = UnoVibe.Polyfills.MacOS.FolderPicker;

using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;

namespace UnoVibe.Polyfills.MacOS;

/// <summary>
/// macOS implementation of the Windows App SDK <c>Microsoft.Windows.Storage.Pickers.FolderPicker</c>
/// (see https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.storage.pickers.folderpicker).
/// Unlike Uno's built-in <c>Windows.Storage.Pickers.FolderPicker</c>, <see cref="SuggestedStartFolder"/>
/// is honored — the panel opens at an arbitrary path — by driving <c>NSOpenPanel</c> directly through
/// the Objective-C runtime (<c>objc_msgSend</c>), setting <c>directoryURL</c> to the requested folder.
/// </summary>
public sealed class FolderPicker
{
    public FolderPicker(Window window)
    {
    }

    /// <summary>Label for the accept button; empty uses the system default.</summary>
    public string CommitButtonText { get; set; } = string.Empty;

    /// <summary>Persists the panel's last location across invocations. A null/empty value
    /// lets the panel restore its own state.</summary>
    public string SettingsIdentifier { get; set; } = string.Empty;

    /// <summary>The folder the picker always tries to display, when no exact
    /// <see cref="SuggestedStartFolder"/> is set. The exact path wins.</summary>
    public string SuggestedFolder { get; set; } = string.Empty;

    /// <summary>The suggested start folder the dialog displays when it opens (exact path).</summary>
    public string SuggestedStartFolder { get; set; } = string.Empty;

    /// <summary>Initial location fallback used when no explicit start path is set.</summary>
    public PickerLocationId SuggestedStartLocation { get; set; } = PickerLocationId.Unspecified;

    /// <summary>Panel title; empty uses the system default (macOS panels usually hide it).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Displays the platform folder-picker dialog. Returns the picked folder's path wrapped
    /// in a <see cref="PickFolderResult"/>, or null when the user cancels. Native failures throw.
    /// </summary>
    public Task<PickFolderResult?> PickSingleFolderAsync() => PickSingleFolderAsyncCore();

    private async Task<PickFolderResult?> PickSingleFolderAsyncCore()
    {
        var path = await RunOnMain(() => NativePickSingleFolder());
        return path is null ? null : new PickFolderResult(path);
    }

    /// <summary>Always enqueue — running <c>NSOpenPanel.runModal</c> reentrantly from an in-flight
    /// pointer handler crashes AppKit's event machinery (see Uno's MacOSFolderPickerExtension). The
    /// modal panel runs its own event loop on the main thread, so the app keeps responding.
    /// <c>DispatcherQueue.GetForCurrentThread()</c> wraps Uno's main UI dispatcher (native
    /// <c>AppWindow.DispatcherQueue</c> is unimplemented on Skia), so it must be called from the
    /// UI thread — which every picker call site is. The queue posts to the main dispatcher even
    /// when we're already on it, avoiding the reentrancy above.</summary>
    private Task<string?> RunOnMain(Func<string?> action)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Run()
        {
            try
            {
                tcs.TrySetResult(action());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        if (DispatcherQueue.GetForCurrentThread() is { } queue)
        {
            queue.TryEnqueue(Run);
        }
        else
        {
            // Not on the UI thread (or no dispatcher yet) — run inline as a best effort.
            Run();
        }

        return tcs.Task;
    }

    private string? NativePickSingleFolder()
    {
        var panel = ObjC.msgSend(ObjC.Class("NSOpenPanel"), ObjC.Selector("openPanel"));

        ObjC.msgSendSetBool(panel, ObjC.Selector("setCanChooseDirectories:"), true);
        ObjC.msgSendSetBool(panel, ObjC.Selector("setCanChooseFiles:"), false);
        ObjC.msgSendSetBool(panel, ObjC.Selector("setAllowsMultipleSelection:"), false);

        var startFolder = ResolveStartFolder();
        if (startFolder.Length > 0 && Directory.Exists(startFolder))
        {
            ObjC.msgSendSetObj(panel, ObjC.Selector("setDirectoryURL:"), ObjC.FileUrl(startFolder));
        }

        if (CommitButtonText.Length > 0)
        {
            ObjC.msgSendSetObj(panel, ObjC.Selector("setPrompt:"), ObjC.NSString(CommitButtonText));
        }
        if (Title.Length > 0)
        {
            ObjC.msgSendSetObj(panel, ObjC.Selector("setTitle:"), ObjC.NSString(Title));
        }
        if (SettingsIdentifier.Length > 0)
        {
            ObjC.msgSendSetObj(panel, ObjC.Selector("setIdentifier:"), ObjC.NSString(SettingsIdentifier));
        }

        var modalResponse = ObjC.msgSendLong(panel, ObjC.Selector("runModal"));
        if (modalResponse != (long)NSModalResponse.OK)
        {
            return null; // user cancelled
        }

        var url = ObjC.msgSend(panel, ObjC.Selector("URL"));
        if (url == IntPtr.Zero)
        {
            return null;
        }

        var path = ObjC.msgSend(url, ObjC.Selector("path"));
        return ObjC.Utf8String(path);
    }

    /// <summary>Resolves the directory the panel should open at: the exact start path
    /// (<see cref="SuggestedStartFolder"/>, then <see cref="SuggestedFolder"/>) if it exists,
    /// else the <see cref="SuggestedStartLocation"/> mapping. Empty means "let the panel decide".</summary>
    private string ResolveStartFolder()
    {
        if (SuggestedStartFolder.Length > 0 && Directory.Exists(SuggestedStartFolder))
        {
            return SuggestedStartFolder;
        }
        if (SuggestedFolder.Length > 0 && Directory.Exists(SuggestedFolder))
        {
            return SuggestedFolder;
        }
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return SuggestedStartLocation switch
        {
            PickerLocationId.Desktop => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            PickerLocationId.DocumentsLibrary => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            PickerLocationId.MusicLibrary => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            PickerLocationId.PicturesLibrary => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            PickerLocationId.VideosLibrary => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            PickerLocationId.Downloads => Path.Combine(profile, "Downloads"),
            PickerLocationId.ComputerFolder => "/",
            PickerLocationId.HomeGroup or PickerLocationId.Objects3D => profile,
            _ => string.Empty
        };
    }

    private enum NSModalResponse : long
    {
        OK = 1,
        Cancel = 0
    }
}

/// <summary>
/// Thin, AOT-safe Objective-C runtime binder for the handful of AppKit messages the macOS
/// polyfill needs (libobjc is part of macOS, so no extra dependency). Messages return either an
/// object pointer (<see cref="msgSend"/>), an NSInteger (<see cref="msgSendLong"/>), or nothing
/// (the setters); strings are marshalled as UTF-8 C pointers for <c>NSString::stringWithUTF8String:</c>.
/// </summary>
static partial class ObjC
{
    public static IntPtr Class(string name) => objc_getClass(name);
    public static IntPtr Selector(string name) => sel_registerName(name);

    /// <summary>Sends a message returning an object pointer: <c>+openPanel</c>, <c>-URL</c>, <c>-path</c>, <c>-UTF8String</c>.</summary>
    public static IntPtr msgSend(IntPtr receiver, IntPtr selector) => objc_msgSend(receiver, selector);

    /// <summary>Sends a message returning an NSInteger: <c>-runModal</c>.</summary>
    public static long msgSendLong(IntPtr receiver, IntPtr selector) => objc_msgSendLong(receiver, selector);

    /// <summary>Sends a message taking a C bool (1 byte) and void return: the <c>setCan*:</c> setters.</summary>
    public static void msgSendSetBool(IntPtr receiver, IntPtr selector, bool value) =>
        objc_msgSendSetBool(receiver, selector, value);

    /// <summary>Sends a message taking one object pointer: <c>setDirectoryURL:</c>, <c>setPrompt:</c>, …</summary>
    public static void msgSendSetObj(IntPtr receiver, IntPtr selector, IntPtr value) =>
        objc_msgSendSetObj(receiver, selector, value);

    /// <summary>Creates an autoreleased <c>NSString</c> from a managed string.</summary>
    public static IntPtr NSString(string? value) =>
        value is null or { Length: 0 }
            ? IntPtr.Zero
            : objc_msgSendString(Class(NS_CLASS_NSString), Selector("stringWithUTF8String:"), value);

    /// <summary>Creates an autoreleased <c>NSURL</c> pointing at <paramref name="path"/>.</summary>
    public static IntPtr FileUrl(string path)
    {
        var nsString = NSString(path);
        return objc_msgSendObj(Class(NS_CLASS_NSURL), Selector("fileURLWithPath:"), nsString);
    }

    /// <summary>Copies an <c>NSString</c>'s UTF-8 content into a managed string.</summary>
    public static string? Utf8String(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero)
        {
            return null;
        }

        var utf8 = objc_msgSend(nsString, Selector("UTF8String"));
        return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
    }

    private const string NS_CLASS_NSString = "NSString";
    private const string NS_CLASS_NSURL = "NSURL";

    [LibraryImport("libobjc.A.dylib", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getClass(string name);

    [LibraryImport("libobjc.A.dylib", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr sel_registerName(string name);

    [LibraryImport("libobjc.A.dylib")]
    private static partial IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [LibraryImport("libobjc.A.dylib")]
    private static partial long objc_msgSendLong(IntPtr receiver, IntPtr selector);

    [LibraryImport("libobjc.A.dylib")]
    private static partial void objc_msgSendSetBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);

    [LibraryImport("libobjc.A.dylib")]
    private static partial void objc_msgSendSetObj(IntPtr receiver, IntPtr selector, IntPtr value);

    [LibraryImport("libobjc.A.dylib", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_msgSendString(IntPtr receiver, IntPtr selector, string value);

    [LibraryImport("libobjc.A.dylib")]
    private static partial IntPtr objc_msgSendObj(IntPtr receiver, IntPtr selector, IntPtr value);
}
#endif