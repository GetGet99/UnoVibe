using UnoVibe.Integration;
using UnoVibe.Integration.Events;
using UnoVibe.Models;
namespace UnoVibe.Pages.Main;

class McpService
{
    // Compact "N active, M inactive, K error" summary for the collapsed MCP sidebar header.
    // inactive = explicitly disabled; error = failed/needs_auth/needs_client_registration (mutually exclusive).
    public Reference<string> SummaryProp { get; } = new("");
    public string Summary
    {
        get => SummaryProp.Value;
        private set => SummaryProp.Value = value;
    }

    // Guards concurrent connect/disconnect requests (one toggle at a time).
    private bool _mcpBusy;
    // Background poll is only active while the sidebar MCP section is expanded.
    private volatile bool _polling;
    public bool Polling
    {
        get => _polling;
        set => _polling = value;
    }
    private const int McpPollIntervalMs = 5000;
    OpencodeClient Client { get; }
    EventsProvider Events { get; }
    public string Directory { get; }
    ToastsProvider Toasts { get; }
    DispatcherQueue Dispatcher { get; }
    CancellationTokenSource cts = new();
    CancellationToken ct => cts.Token;
    public McpService(OpencodeClient client, EventsProvider events, ToastsProvider toasts, DispatcherQueue dispatcher, string directory)
    {
        Client = client;
        Directory = directory;
        Events = events;
        Toasts = toasts;
        Dispatcher= dispatcher;
        
        Events.RegisterMcpToolsChanged(Directory, McpToolsChangedHandler);
        _ = Task.Run(() => McpPollLoopAsync());
        _ = RefreshMcpStatusAsync();
    }
    void McpToolsChangedHandler(string d, McpToolsChangedEvent _1)
    {
        if (d == Directory) _ = RefreshMcpStatusAsync();
    }

    public ObservableCollection<McpServerItem> Servers { get; } = [];
    // O(1) lookup index for Servers by name, kept in sync with the ObservableCollection
    // so ApplyMcpStatus can reconcile in place instead of a Clear+re-Add rebuild.
    private readonly Dictionary<string, McpServerItem> _mcpServersByName = new();


    /// <summary>
    /// Refreshes the MCP server list from GET /mcp for the active session's directory.
    /// MCP status is per workspace directory (instance), not per session, so the sidebar
    /// reflects whichever session is currently open. When there is no session yet, falls
    /// back to the pending/current directory.
    /// </summary>
    public async Task RefreshMcpStatusAsync()
    {
        if (!(await Client.GetMcpStatusAsync(Directory, ct)).TryGetValue(out var status, out var error))
        {
            Toasts.ShowError(error, "Could not refresh MCP status");
            return;
        }
        ApplyMcpStatus(status);
        var connected = status.Values.Count(s => s.Status == "connected");
        var inactive = status.Values.Count(s => s.Status == "disabled");
        var bad = status.Values.Count(s => s.Status is "failed" or "needs_auth" or "needs_client_registration");
        var summaryParts = new List<string>();
        if (connected > 0) summaryParts.Add($"{connected} active");
        if (inactive > 0) summaryParts.Add($"{inactive} inactive");
        if (bad > 0) summaryParts.Add($"{bad} error");
        Summary = summaryParts.Count > 0 ? string.Join(", ", summaryParts) : "none";
    }

    /// <summary>
    /// Reconciles <see cref="Servers"/> against the server's GET /mcp report in place
    /// (the sidebar poll runs every few seconds while the MCP section is expanded):
    /// servers the server no longer reports are removed, existing ones keep their item
    /// (and any in-flight toggle state) with Status/Error updated, and new ones are
    /// inserted in name order — no Clear+re-Add rebuild.
    /// </summary>
    private void ApplyMcpStatus(Dictionary<string, McpStatusInfo> status)
    {
        for (var i = Servers.Count - 1; i >= 0; i--)
        {
            if (status.ContainsKey(Servers[i].Name)) continue;
            _mcpServersByName.Remove(Servers[i].Name);
            Servers.RemoveAt(i);
        }

        foreach (var kv in status.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (_mcpServersByName.TryGetValue(kv.Key, out var existing))
            {
                existing.Status = kv.Value.Status;
                existing.Error = kv.Value.Error ?? "";
                continue;
            }
            var item = new McpServerItem(Name: kv.Key, Error: kv.Value.Error ?? "") { Status = kv.Value.Status };
            _mcpServersByName[kv.Key] = item;
            var index = 0;
            while (index < Servers.Count && StringComparer.OrdinalIgnoreCase.Compare(Servers[index].Name, kv.Key) < 0) index++;
            Servers.Insert(index, item);
        }
    }

    /// <summary>
    /// Connects, disconnects, or authenticates an MCP server based on its current status, then
    /// refreshes the list. Mirrors the web client's <c>toggleMcp</c>: connected → disconnect,
    /// needs_auth → authenticate (OAuth), anything else → connect. A needs_auth server has no
    /// usable client yet — the server routes <c>POST /mcp/{name}/auth/authenticate</c>, which
    /// opens the browser on the authorization URL and blocks until the OAuth callback completes.
    /// </summary>
    public async Task ToggleMcpAsync(string name)
    {
        if (_mcpBusy) return;
        if (!_mcpServersByName.TryGetValue(name, out var server)) return;
        _mcpBusy = true;
        server.Connecting = true;
        try
        {
            if (server.IsConnected)
            {
                await Client.McpDisconnectAsync(name, Directory);
            }
            else if (server.NeedsAuth)
            {
                Toasts.Show(new ToastItem
                {
                    Title = $"Authenticating {name}",
                    Message = "Complete the sign-in in the browser that opened to connect this MCP server.",
                    Variant = "info",
                    DurationMs = 8000,
                });
                if (!(await Client.McpAuthenticateAsync(name, Directory)).TryGetValue(out var result, out var error))
                {
                    Toasts.ShowError(error, $"MCP {name} auth failed");
                } else
                {
                    if (result.Status == "failed" && result.Error?.Length > 0)
                        Toasts.ShowError(result.Error, $"MCP {name} auth failed");
                }
            }
            else
            {
                await Client.McpConnectAsync(name, Directory);
            }
        }
        catch (Exception ex)
        {
            Toasts.ShowError(ex.Message, "MCP toggle failed");
        }
        finally
        {
            server.Connecting = false;
            _mcpBusy = false;
        }
        await RefreshMcpStatusAsync();
    }

    /// <summary>
    /// Background poll: re-fetches GET /mcp every few seconds while enabled. The server
    /// pushes no MCP status event (only mcp.tools.changed, without status), so expanded
    /// sections need periodic polling to stay live. Runs on a background thread and hops
    /// to the UI dispatcher for the actual refresh, since McpServers/McpSummary are
    /// reactive references.
    /// </summary>
    private async Task McpPollLoopAsync()
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(McpPollIntervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            if (!Polling) continue;
            Dispatcher.TryEnqueue(() => _ = RefreshMcpStatusAsync());
        }
    }
    public void Dispose()
    {
        cts.Cancel();
        Events.UnregisterMcpToolsChanged(Directory, McpToolsChangedHandler);
    }
}
