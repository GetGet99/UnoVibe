using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace UnoVibe.Models;

sealed class ImageAttachment
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = "";
    public string Mime { get; set; } = "image/png";
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
    public BitmapImage? Preview { get; set; }

    public string DataUrl => $"data:{Mime};base64,{Convert.ToBase64String(Bytes)}";

    public static async Task<BitmapImage?> DecodeAsync(byte[] bytes)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
            }
            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public static string MimeFromPath(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/png",
        };
    }

    public static async Task<ImageAttachment> CreateFromBytesAsync(byte[] bytes, string mime, string fileName)
    {
        return new ImageAttachment
        {
            FileName = fileName,
            Mime = mime,
            Bytes = bytes,
            Preview = await DecodeAsync(bytes),
        };
    }

    public static async Task<ImageAttachment> CreateFromFileAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        return new ImageAttachment
        {
            FileName = Path.GetFileName(path),
            Mime = MimeFromPath(path),
            Bytes = bytes,
            Preview = await DecodeAsync(bytes),
        };
    }
}
