#if DESKTOP_LINUX
// Registers the Linux polyfill `FolderPicker` as the app-wide `FolderPicker` type, so
// `WindowsHelper.PickFolderAsync` (and any other caller) can use the WASDK-shaped API without
// platform #if. (`PickFolderResult` is registered in PickFolderResult.cs.) See AGENTS.md Polyfills.
global using FolderPicker = UnoVibe.Polyfills.Linux.FolderPicker;

using System.Text;
using Tmds.DBus.Protocol;
using UnoVibe.Polyfills.Linux.DBus;
using Windows.Storage.Pickers;

namespace UnoVibe.Polyfills.Linux;

/// <summary>
/// Linux implementation of the Windows App SDK <c>Microsoft.Windows.Storage.Pickers.FolderPicker</c>
/// (see https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.storage.pickers.folderpicker).
/// Unlike Uno's built-in <c>Windows.Storage.Pickers.FolderPicker</c>, <see cref="SuggestedStartFolder"/>
/// is honored — the dialog opens at an arbitrary path — via the XDG desktop portal's
/// <c>org.freedesktop.portal.FileChooser</c> <c>current_folder</c> option, over the session D-Bus.
/// </summary>
public sealed class FolderPicker
{
    private const string Service = "org.freedesktop.portal.Desktop";
    private const string ObjectPath = "/org/freedesktop/portal/desktop";
    private const string ResultObjectPathPrefix = "/org/freedesktop/portal/desktop/request";

    /// <summary>Creates the picker bound to a window, mirroring the WASDK constructor that takes
    /// a window id. The Linux portal dialog is shown by the portal (its own process); the window
    /// is currently only kept for WASDK API parity.</summary>
    public FolderPicker(Window window)
    {
    }

    /// <summary>Label for the accept button; empty uses the portal's default.</summary>
    public string CommitButtonText { get; set; } = string.Empty;

    /// <summary>The folder the dialog always tries to display when it opens, when no exact
    /// <see cref="SuggestedStartFolder"/> is set. Kept for WASDK parity; the exact path wins.</summary>
    public string SuggestedFolder { get; set; } = string.Empty;

    /// <summary>The suggested start folder the dialog displays when it opens (exact path).</summary>
    public string SuggestedStartFolder { get; set; } = string.Empty;

    /// <summary>Initial location fallback used when no explicit start path is set.</summary>
    public PickerLocationId SuggestedStartLocation { get; set; } = PickerLocationId.Unspecified;

    /// <summary>Dialog title; empty uses a default.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Displays the platform folder-picker dialog. Returns the picked folder's path wrapped
    /// in a <see cref="PickFolderResult"/>, or null when the user cancels. Infrastructure
    /// failures (no session bus, missing/old portal, D-Bus error) throw.
    /// </summary>
    public async Task<PickFolderResult?> PickSingleFolderAsync()
    {
        var path = await PickFolderPathAsync();
        return path is null ? null : new PickFolderResult(path);
    }

    private async Task<string?> PickFolderPathAsync()
    {
        var sessionsAddressBus = DBusAddress.Session;
        if (sessionsAddressBus is null)
        {
            throw new InvalidOperationException(
                "Can not determine the DBus session bus address. Is a desktop session active?");
        }

        using var connection = new DBusConnection(sessionsAddressBus);
        // ConnectAsync does ConfigureAwait(false); the continuation after that is on a pool thread,
        // which is fine here — the portal dialog is its own process, it does not need the UI thread.
        await connection.ConnectAsync();

        var desktopService = new DBusService(connection, Service);
        var chooser = desktopService.CreateFileChooser(ObjectPath);

        var version = await chooser.GetVersionAsync();
        if (version < 3)
        {
            throw new NotSupportedException(
                $"The FileChooser portal needs version 3+, but version {version} was found.");
        }

        var handleToken = "UnoVibeFolder" + Random.Shared.NextInt64();
        var requestPath = $"{ResultObjectPathPrefix}/{connection.UniqueName![1..].Replace(".", "_")}/{handleToken}";

        // Subscribe to the Response signal BEFORE calling OpenFile — the portal spec warns
        // of a race where the user can answer the dialog before the subscription lands.
        // The connection is disposed once we return, which also drops the subscription.
        var responseTcs = new TaskCompletionSource<(uint Response, Dictionary<string, VariantValue> Results)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var request = desktopService.CreateRequest(requestPath);
        _ = request.WatchResponseAsync((exception, tuple) =>
        {
            if (exception is not null)
                responseTcs.TrySetException(exception);
            else
                responseTcs.TrySetResult(tuple);
        });

        // parent_window: empty string is the documented "no parent" — the real X11 window id is
        // not exposed by Uno's public API (its portal picker gets it through InternalsVisibleTo-level
        // access to X11XamlRootHost). The dialog still opens; it is just not made modal to our window.
        var actualRequestPath = await chooser.OpenFileAsync(
            parentWindow: string.Empty,
            title: string.IsNullOrEmpty(Title) ? "Select a Folder" : Title,
            options: BuildOptions(handleToken));

        if (actualRequestPath != requestPath)
        {
            throw new InvalidOperationException(
                $"{nameof(chooser.OpenFileAsync)} returned request path '{actualRequestPath}' " +
                $"different from the handle_token-based '{requestPath}'.");
        }

        var (response, results) = await responseTcs.Task;

        switch ((PortalResponse)response)
        {
            case PortalResponse.Success:
            {
                // The portal returns file:// URIs of the selected files/folders.
                return results["uris"].GetArray<string>()
                    .Select(uri => new Uri(uri).LocalPath)
                    .FirstOrDefault();
            }
            case PortalResponse.UserCancelled:
                return null;
            default:
                throw new InvalidOperationException(
                    $"The FileChooser portal reported an unsuccessful response {response}.");
        }
    }

    private Dictionary<string, VariantValue> BuildOptions(string handleToken)
    {
        var options = new Dictionary<string, VariantValue>
        {
            { "handle_token", handleToken },
            { "accept_label", string.IsNullOrEmpty(CommitButtonText) ? "Select" : CommitButtonText },
            { "multiple", false },
            { "directory", true }
        };

        var startFolder = ResolveStartFolder();
        if (startFolder.Length > 0)
        {
            // current_folder: byte array of the filesystem-encoded path, NUL terminated.
            options["current_folder"] = new Array<byte>(
                Encoding.UTF8.GetBytes(startFolder).Append((byte)'\0'));
        }

        return options;
    }

    /// <summary>Resolves the directory the dialog should open at: the exact start path
    /// (<see cref="SuggestedStartFolder"/>, then <see cref="SuggestedFolder"/>) if it exists,
    /// else the <see cref="SuggestedStartLocation"/> mapping. Empty means "let the portal decide".</summary>
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
        return PickerLocationPath(SuggestedStartLocation);
    }

    /// <summary>Maps a WinUI <see cref="PickerLocationId"/> to a concrete path (mirrors Uno's
    /// <c>Uno.UI.Helpers.PickerHelpers.GetInitialDirectory</c>).</summary>
    private static string PickerLocationPath(PickerLocationId location) =>
        location switch
        {
            PickerLocationId.Desktop => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            PickerLocationId.DocumentsLibrary => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            PickerLocationId.MusicLibrary => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            PickerLocationId.PicturesLibrary => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            PickerLocationId.VideosLibrary => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            PickerLocationId.Downloads => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/Downloads",
            PickerLocationId.ComputerFolder => "/",
            _ => string.Empty
        };
}

/// <summary>Response codes of the <c>org.freedesktop.portal.Request</c> Response signal.</summary>
internal enum PortalResponse : uint
{
    Success = 0,
    UserCancelled = 1,
    Other = 2
}
#endif