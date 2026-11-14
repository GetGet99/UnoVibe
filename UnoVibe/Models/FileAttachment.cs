namespace UnoVibe.Models;

sealed class FileAttachment
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Mime { get; set; } = "text/plain";
    public string Url => new Uri(Path).AbsoluteUri;
}
