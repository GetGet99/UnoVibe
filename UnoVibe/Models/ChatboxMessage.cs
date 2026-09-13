using QuickMarkup.Infra.Collections;

namespace UnoVibe.Models;

[QuickMarkup("""
    string Text = "";
    """)]
public partial class ChatboxMessage
{
    /// <summary>Image attachments staged for the next prompt (shown as thumbnails above the input).</summary>
    public ReactiveList<ImageAttachment> Images { get; } = [];

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text) && Images.Count is 0;


    /// <summary>
    /// Restores the user message's prompt into the composer: concatenated non-synthetic
    /// text parts (TUI skips synthetic) plus its data-URL image file parts re-staged as pending
    /// attachments. Matches the TUI/web undo behavior.
    /// </summary>
    public static ChatboxMessage From(MessageItem message)
    {
        var msg = new ChatboxMessage();
        var sb = new System.Text.StringBuilder();
        foreach (var part in message.Parts)
        {
            if (part.Type == "text" && !part.Synthetic) sb.Append(part.Text);
            else if (part.Type == "file")
            {
                var attachment = AttachmentFromPart(part);
                if (attachment is null) continue;
                msg.Images.Add(attachment);
            }
        }
        msg.Text =  sb.ToString();
        return msg;
    }

    /// <summary>Rebuilds an <see cref="ImageAttachment"/> from a data-URL image file part; null when not decodable.</summary>
    static ImageAttachment? AttachmentFromPart(PartItem part)
    {
        if (!part.IsImage || !part.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
        var comma = part.Url.IndexOf(',');
        if (comma < 0) return null;
        try
        {
            var bytes = Convert.FromBase64String(part.Url[(comma + 1)..]);
            var attachment = new ImageAttachment
            {
                FileName = part.FileName.Length > 0 ? part.FileName : "attachment",
                Mime = part.Mime.Length > 0 ? part.Mime : "image/png",
                Bytes = bytes,
            };
            // Decode fire-and-forget like PartItem.LoadImageAsync; the await resumes on the
            // UI thread so the thumbnail strip updates once the bitmap is ready.
            _ = DecodePreviewAsync(attachment);
            return attachment;
        }
        catch
        {
            return null;
        }
    }

    static async Task DecodePreviewAsync(ImageAttachment attachment)
    {
        attachment.Preview = await ImageAttachment.DecodeAsync(attachment.Bytes);
    }
}