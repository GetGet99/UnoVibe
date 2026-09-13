using UnoVibe.Integration;
using UnoVibe.Services;

namespace UnoVibe.States;
public partial class OpencodeConnection : IDisposable
{
    private Reference<string> ConnectionStatusProp = new("Connecting...");
    public string ConnectionStatus { get => ConnectionStatusProp.Value; private set => ConnectionStatusProp.Value = value; }
    public string BaseUrl { get; }
    public string? Password { get; }
    public string? Username { get; }
    public OpencodeClient Client { get; }
    public OpencodeServeProcess? ServeProcess { get; private set; }
    public string ServerDirectory { get; private set; } = null!;
    public string DisplayLabel => $"{BaseUrl} - {ServerDirectory}";
    private OpencodeConnection(string baseUrl, string? username, string? password)
    {
        BaseUrl = baseUrl.Trim().TrimEnd('/');
        Username = username ?? "opencode";
        if (string.IsNullOrEmpty(Username)) Username = null;
        if (string.IsNullOrEmpty(Password)) Password = null;
        Client = new OpencodeClient(baseUrl, password, username);
    }

    public static async Task<OpencodeConnection> FromAsync(OpencodeServeProcess process, CancellationToken ct = default)
    {
        var conn = new OpencodeConnection(process.BaseUrl, null, process.Password) { ServeProcess =  process };
        await conn.ConnectAsync(ct);
        return conn;
    }

    public static async Task<OpencodeConnection> FromAsync(string baseUrl, string? username, string? password, CancellationToken ct = default)
    {
        var conn = new OpencodeConnection(baseUrl, username, password);
        await conn.ConnectAsync(ct);
        return conn;
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        try
        {
            var healthResult = await Client.HealthAsync(ct);
            if (healthResult.GetOr(false))
            {
                ConnectionStatus = "Connected";
                if (ServeProcess is not null)
                {
                    // Folder launch: use the folder we started serve in.
                    ServerDirectory = ServeProcess.WorkingDirectory;
                }
                else
                {
                    // URL connection: fetch the server's default directory.
                    var path = (await Client.GetPathAsync(ct)).GetOrThrow();
                    if (path.Directory is { Length: > 0 } dir)
                    {
                        ServerDirectory = dir;
                    }
                }
            }
            else if (!healthResult.IsSuccess)
            {
                if (healthResult.Error.StatusCode is System.Net.HttpStatusCode.Unauthorized)
                    ConnectionStatus = "Error: unauthorized - check the server password";
                else
                    ConnectionStatus = $"Error: {healthResult.Error.Message}";
            }
            else
            {
                ConnectionStatus = "Error: health check failed";
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error: {ex.Message}";
            return;
        }
    }
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        ServeProcess?.Dispose();
        Client.Dispose();
    }
}