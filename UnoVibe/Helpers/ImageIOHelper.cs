using Uno.Extensions;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace UnoVibe.Helpers;

static class ImageIOHelper
{

    private static string[] ImageExtensions => field ??= [.. ImageClipboardFormats.Select(x => $".{x.Ext}").Distinct()];

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

    public static async Task<List<ImageAttachment>> PasteImageFromClipboardAsync()
    {
        List<ImageAttachment> images = [];
        try
        {
            var content = Clipboard.GetContent();
            if (content is null) return images;

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

            foreach (var (name, mime, ext) in ImageClipboardFormats)
            {
                var format = 
#if DESKTOP_LINUX
                    mime
#else
                    name
#endif
                    ;
                if (!content.Contains(format)) continue;
                var item = await content.GetDataAsync(format);
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
        }
        return images;
    }

    private static async Task<byte[]> ReadAllBytes(IRandomAccessStream stream)
    {
        stream.Seek(0);
        using var ms = new MemoryStream();
        await stream.AsStreamForRead().CopyToAsync(ms);
        return ms.ToArray();
    }

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