using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace UnoVibe.Services;

/// <summary>
/// Launches `opencode serve` from a chosen working directory and reports the
/// base URL it listens on once it is healthy. The child process is kept alive
/// for the lifetime of this object.
/// </summary>
public sealed class ServeProcess : IDisposable
{
    private Process? _process;

    /// <summary>Base URL of the launched server (set after startup succeeds).</summary>
    public string BaseUrl { get; private set; } = "";

    public bool IsRunning => _process is { HasExited: false };

    public static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Starts `opencode serve` in <paramref name="workingDirectory"/> and waits
    /// until its health endpoint responds. Returns the base URL, or an error message.
    /// </summary>
    public async Task<string> StartAsync(string workingDirectory, CancellationToken ct = default)
    {
        var port = FindFreePort();
        var startInfo = new ProcessStartInfo
        {
            FileName = "opencode",
            Arguments = $"serve --port {port}",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        _process = new Process { StartInfo = startInfo };
        if (!_process.Start())
        {
            BaseUrl = "";
            return "Failed to start opencode process.";
        }

        BaseUrl = $"http://127.0.0.1:{port}";

        using var health = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var response = await health.GetAsync("/global/health", ct);
                if (response.IsSuccessStatusCode) return BaseUrl;
            }
            catch (Exception)
            {
                // Server not up yet.
            }

            if (_process.HasExited)
            {
                var error = _process.StandardError.ReadToEnd();
                return string.IsNullOrWhiteSpace(error)
                    ? "opencode exited before becoming healthy."
                    : $"opencode exited: {error.Trim()}";
            }

            await Task.Delay(500, ct);
        }

        return $"opencode did not become healthy within 30 seconds.";
    }

    public void Dispose()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Best effort.
        }

        _process.Dispose();
        _process = null;
    }
}
