using UnoVibe.Integration;
using UnoVibe.Integration.Events;
namespace UnoVibe.Pages.Main;

class McpService
{
    public Reference<string> SummaryProp { get; } = new("");
    public string Summary
    {
        get => SummaryProp.Value;
        private set => SummaryProp.Value = value;
    }

    private volatile bool _mcpBusy;
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
    private readonly Dictionary<string, McpServerItem> _mcpServersByName = new();

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
