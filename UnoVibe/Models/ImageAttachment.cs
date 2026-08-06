using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace UnoVibe.Models;

/// <summary>
/// A pending image attachment that will be included with the next prompt as a
/// base64 data-URL file part (mirrors how the opencode TUI/web clients attach images).
/// </summary>
public sealed class ImageAttachment
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = "";
    public string Mime { get; set; } = "image/png";
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
    /// <summary>Decoded thumbnail/preview, set before the item is added to the UI collection.</summary>
    public BitmapImage? Preview { get; set; }

    public string DataUrl => $"data:{Mime};base64,{Convert.ToBase64String(Bytes)}";

    /// <summary>Decodes image bytes into a <see cref="BitmapImage"/>; null when the bytes are not a decodable image.</summary>
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

    /// <summary>Guesses the MIME type from a file extension (defaults to PNG for unknown).</summary>
    public static string MimeFromPath(string path)
    {
        return System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/png",
        };
    }
}
