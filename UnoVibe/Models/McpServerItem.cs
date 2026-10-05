namespace UnoVibe.Models;

[QuickRefs("""
    public string Name = "";
    public string Status = "disabled";
    public string Error = "";
    public bool Connecting;
    public bool IsConnected => `Status == "connected"`;
    public bool NeedsAuth => `Status == "needs_auth"`;
    public string StatusLabel => `FormatStatus(Status)`;
    public string ToggleLabel => `IsConnected ? "Disconnect" : NeedsAuth ? (Connecting ? "Authenticating…" : "Authenticate") : Connecting ? "Connecting…" : "Connect"`;
    """)]
public sealed partial class McpServerItem
{
    public McpServerItem(string Name, string Error)
    {
        this.Name = Name;
        this.Error = Error;
    }

    private static string FormatStatus(string status) => status switch
    {
        "connected" => "Connected",
        "disabled" => "Disabled",
        "failed" => "Failed",
        "needs_auth" => "Needs auth",
        "needs_client_registration" => "Needs client ID",
        _ => status,
    };
}
