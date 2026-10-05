#if DESKTOP_LINUX
global using FolderPicker = UnoVibe.Polyfills.Linux.FolderPicker;

using System.Text;
using Tmds.DBus.Protocol;
using UnoVibe.Polyfills.Linux.DBus;
using Windows.Storage.Pickers;

namespace UnoVibe.Polyfills.Linux;

sealed class FolderPicker
{
    private const string Service = "org.freedesktop.portal.Desktop";
    private const string ObjectPath = "/org/freedesktop/portal/desktop";
    private const string ResultObjectPathPrefix = "/org/freedesktop/portal/desktop/request";

    public FolderPicker(Window window)
    {
    }

    public string CommitButtonText { get; set; } = string.Empty;

    public string SuggestedFolder { get; set; } = string.Empty;

    public string SuggestedStartFolder { get; set; } = string.Empty;

    public PickerLocationId SuggestedStartLocation { get; set; } = PickerLocationId.Unspecified;

    public string Title { get; set; } = string.Empty;

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
            options["current_folder"] = new Array<byte>(
                Encoding.UTF8.GetBytes(startFolder).Append((byte)'\0'));
        }

        return options;
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
        return PickerLocationPath(SuggestedStartLocation);
    }

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

internal enum PortalResponse : uint
{
    Success = 0,
    UserCancelled = 1,
    Other = 2
}
#endif