using QuickMarkup.Infra.Collections;

namespace UnoVibe.Models;

[QuickRefs("""
    string Text = "";
    """)]
partial class ChatboxMessage
{
    public ReactiveList<ImageAttachment> Images { get; } = [];

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text) && Images.Count is 0;

    public static ChatboxMessage From(MessageItem message)
    {
        var msg = new ChatboxMessage();
        var sb = new System.Text.StringBuilder();
        foreach (var part in message.Parts)
        {
            if (part is TextPartItem text && !text.Synthetic) sb.Append(text.Text);
            else if (part is FilePartItem file)
            {
                var attachment = AttachmentFromFile(file);
                if (attachment is null) continue;
                msg.Images.Add(attachment);
            }
        }
        msg.Text =  sb.ToString();
        return msg;
    }

    static ImageAttachment? AttachmentFromFile(FilePartItem part)
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