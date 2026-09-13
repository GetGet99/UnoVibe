using Uno.Extensions;
using UnoVibe.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace UnoVibe.Helpers;

static class ImageIOHelper
{

    /// <summary>Image file extensions accepted by the picker and the clipboard storage-items paste path.</summary>
    private static string[] ImageExtensions => field ??= [.. ImageClipboardFormats.Select(x => x.Ext).Distinct()];

    /// <summary>
    /// Clipboard format names probed (in order) when pasting raw image bytes. Covers the union
    /// of what each Skia backend exposes: X11 mime atoms (<c>image/png</c>, <c>image/jpeg</c>,
    /// ...) returning <c>byte[]</c>, and Win32 registered format names (<c>PNG</c>, <c>JFIF</c>,
    /// ...) returning <c>IRandomAccessStream</c>, plus the CF_DIB remap
    /// <c>StandardDataFormats.Bitmap</c> returning a <c>RandomAccessStreamReference</c>.
    /// </summary>
    private static readonly (string Name, string Mime, string Ext)[] ImageClipboardFormats =
    [
        ("image/png", "image/png", "png"),
        ("image/jpeg", "image/jpeg", "jpeg"),
        ("image/gif", "image/gif", "gif"),
        ("image/webp", "image/webp", "webp"),
        ("image/bmp", "image/bmp", "bmp"),
        ("PNG", "image/png", "png"),
        ("JFIF", "image/jpeg", "jpeg"),
        ("JPEG", "image/jpeg", "jpeg"),
        ("GIF", "image/gif", "gif"),
        ("WEBP", "image/webp", "webp"),
        ("BMP", "image/bmp", "bmp"),
        (StandardDataFormats.Bitmap, "image/bmp", "bmp"),
    ];

    /// <summary>
    /// Pastes an image from the system clipboard (Ctrl+V). Returns true when at least one
    /// image was staged; false when the clipboard holds no usable image, so the caller can
    /// let the default text paste proceed.
    /// </summary>
    /// <remarks>
    /// Uses Uno's built-in <see cref="Clipboard"/>, probing the union of what each Skia
    /// backend exposes. On X11 it routes to the <c>X11ClipboardExtension</c> (raw
    /// <c>image/png</c>/<c>image/jpeg</c> atoms returning <c>byte[]</c>, files via
    /// <c>text/uri-list</c>); on Windows to the <c>Win32ClipboardExtension</c> (registered
    /// format names like <c>PNG</c>/<c>JFIF</c> returning <c>IRandomAccessStream</c>, CF_DIB
    /// remapped to <c>StandardDataFormats.Bitmap</c>, files via <c>CF_HDROP</c>). Both expose
    /// files under <c>StandardDataFormats.StorageItems</c>, so that check is shared. Only the
    /// read path is needed here; the write path workaround from PocketPic is not required.
    /// </remarks>
    public static async Task<List<ImageAttachment>> PasteImageFromClipboardAsync()
    {
        List<ImageAttachment> images = [];
        try
        {
            var content = Clipboard.GetContent();
            if (content is null) return images;

            // Files first: "Shell IDList Array" is the cross-platform storage-items format
            // (X11 maps text/uri-list to it; Win32 maps CF_HDROP to it).
            if (content.Contains(StandardDataFormats.StorageItems))
            {
                var items = await content.GetStorageItemsAsync();
                foreach (var item in items)
                {
                    if (item is not StorageFile file) continue;
                    var ext = Path.GetExtension(file.Path).ToLowerInvariant();
                    if (ImageExtensions.Contains(ext))
                    {
                        images.Add(await ImageAttachment.CreateFromFileAsync(file.Path));
                    }
                }
                if (images.Count > 0) return images;
            }

            // Raw image bytes: probe the union of format names each platform exposes. The
            // retrieved value may be byte[] (X11), IRandomAccessStream (Win32 registered
            // format), or RandomAccessStreamReference (Win32 CF_DIB).
            foreach (var (name, mime, ext) in ImageClipboardFormats)
            {
                if (!content.Contains(name)) continue;
                var item = await content.GetDataAsync(name);
                byte[]? bytes = item switch
                {
                    byte[] raw => raw,
                    IRandomAccessStream stream => await ReadAllBytes(stream),
                    IRandomAccessStreamReference streamRef => await ReadAllBytes(await streamRef.OpenReadAsync()),
                    _ => null,
                };
                if (bytes is { Length: > 0 })
                {
                    images.Add(await ImageAttachment.CreateFromBytesAsync(bytes, mime, $"Pasted image.{ext}"));
                }
            }
        }
        catch
        {
            // Foreign clipboard formats or an unavailable selection should not crash paste.
        }
        return images;
    }

    private static async Task<byte[]> ReadAllBytes(IRandomAccessStream stream)
    {
        // DataReader.LoadAsync is not implemented in Uno (Uno0001), so read the underlying
        // stream instead: AsStreamForRead unwraps the MemoryStream-backed IRandomAccessStream
        // that Uno's clipboard extensions produce (the same pattern Win32ClipboardExtension uses).
        stream.Seek(0);
        using var ms = new MemoryStream();
        await stream.AsStreamForRead().CopyToAsync(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Opens the native file picker and stages the chosen image as a pending attachment.
    /// <paramref name="window"/> is the hosting window used to initialize the picker (WinRT
    /// pickers need an HWND on Windows).
    /// Returns null when user cancelled the operation.
    /// </summary>
    public static async Task<List<ImageAttachment>> PickImagesAsync(Window window)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
        };
        picker.FileTypeFilter.AddRange(ImageExtensions);
        WindowsHelper.InitializeWithWindow(picker, window);
        var files = await picker.PickMultipleFilesAsync();
        if (files is null) return [];
        List<ImageAttachment> images = new(files.Count);
        foreach (var file in files)
        {
            images.Add(await ImageAttachment.CreateFromFileAsync(file.Path));
        }
        return images;
    }
}