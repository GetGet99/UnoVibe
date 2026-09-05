namespace UnoVibe.Models;

/// <summary>
/// An MCP server's runtime status as reported by <c>GET /mcp</c>. Status is per
/// workspace directory (instance), not per session: all sessions in a directory
/// share the same MCP servers. Reactive display fields are QuickMarkup references.
/// </summary>
[QuickMarkup("""
    public required string Name;
    // One of "connected" | "disabled" | "failed" | "needs_auth" | "needs_client_registration".
    public string Status = "disabled";
    // Error message carried by the "failed"/"needs_client_registration" statuses.
    public required string Error;
    // True while a connect/disconnect/auth request for this server is in flight.
    public bool Connecting;
    public bool IsConnected => `Status == "connected"`;
    public bool NeedsAuth => `Status == "needs_auth"`;
    public string StatusLabel => `FormatStatus(Status)`;
    public string ToggleLabel => `IsConnected ? "Disconnect" : NeedsAuth ? (Connecting ? "Authenticating…" : "Authenticate") : Connecting ? "Connecting…" : "Connect"`;
    """)]
public sealed partial class McpServerItem
{
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
