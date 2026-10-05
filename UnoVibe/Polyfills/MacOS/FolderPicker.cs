#if DESKTOP_MACOS
global using FolderPicker = UnoVibe.Polyfills.MacOS.FolderPicker;

using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;

namespace UnoVibe.Polyfills.MacOS;

sealed class FolderPicker
{
    public FolderPicker(Window window)
    {
    }

    public string CommitButtonText { get; set; } = string.Empty;

    public string SettingsIdentifier { get; set; } = string.Empty;

    public string SuggestedFolder { get; set; } = string.Empty;

    public string SuggestedStartFolder { get; set; } = string.Empty;

    public PickerLocationId SuggestedStartLocation { get; set; } = PickerLocationId.Unspecified;

    public string Title { get; set; } = string.Empty;

    public Task<PickFolderResult?> PickSingleFolderAsync() => PickSingleFolderAsyncCore();

    private async Task<PickFolderResult?> PickSingleFolderAsyncCore()
    {
        var path = await RunOnMain(() => NativePickSingleFolder());
        return path is null ? null : new PickFolderResult(path);
    }

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
            return null;
        }

        var url = ObjC.msgSend(panel, ObjC.Selector("URL"));
        if (url == IntPtr.Zero)
        {
            return null;
        }

        var path = ObjC.msgSend(url, ObjC.Selector("path"));
        return ObjC.Utf8String(path);
    }

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

static partial class ObjC
{
    public static IntPtr Class(string name) => objc_getClass(name);
    public static IntPtr Selector(string name) => sel_registerName(name);

    public static IntPtr msgSend(IntPtr receiver, IntPtr selector) => objc_msgSend(receiver, selector);

    public static long msgSendLong(IntPtr receiver, IntPtr selector) => objc_msgSendLong(receiver, selector);

    public static void msgSendSetBool(IntPtr receiver, IntPtr selector, bool value) =>
        objc_msgSendSetBool(receiver, selector, value);

    public static void msgSendSetObj(IntPtr receiver, IntPtr selector, IntPtr value) =>
        objc_msgSendSetObj(receiver, selector, value);

    public static IntPtr NSString(string? value) =>
        value is null or { Length: 0 }
            ? IntPtr.Zero
            : objc_msgSendString(Class(NS_CLASS_NSString), Selector("stringWithUTF8String:"), value);

    public static IntPtr FileUrl(string path)
    {
        var nsString = NSString(path);
        return objc_msgSendObj(Class(NS_CLASS_NSURL), Selector("fileURLWithPath:"), nsString);
    }

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