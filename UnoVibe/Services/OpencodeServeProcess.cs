using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
namespace UnoVibe.Services;

sealed class OpencodeServeProcess : IDisposable
{
    private const string PasswordChars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%^&*()-_=+[]{};:,.?";
    private Process? _process;

    public string BaseUrl { get; private set; } = "";

    public string WorkingDirectory { get; private set; } = "";

    public string Password { get; }

    public bool IsRunning => _process is { HasExited: false };

    public static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public OpencodeServeProcess(string? password = null)
    {
        Password = password ?? GeneratePassword(32);
    }

    public static string GeneratePassword(int length = 32)
    {
        var chars = new char[length];

        for (var i = 0; i < length; i++)
            chars[i] = PasswordChars[RandomNumberGenerator.GetInt32(PasswordChars.Length)];

        return new string(chars);
    }

    public async Task<string> StartAsync(string workingDirectory, CancellationToken ct = default)
    {
        var port = FindFreePort();
        var startInfo = new ProcessStartInfo
        {
            FileName = OpencodeExecutable,
            Arguments = $"serve --port {port}",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.Environment[OpencodeHelper.PasswordEnvVar] = Password;
        startInfo.Environment[OpencodeHelper.UsernameEnvVar] = "opencode";

        _process = new Process { StartInfo = startInfo };
        if (!_process.Start())
        {
            BaseUrl = "";
            return "Failed to start opencode process.";
        }

        BaseUrl = $"http://127.0.0.1:{port}";

        using var health = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(2),
        };
        if (Password.Length > 0)
            health.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"opencode:{Password}")));

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var response = await health.GetAsync("/global/health", ct);
                if (response.IsSuccessStatusCode)
                {
                    WorkingDirectory = workingDirectory;
                    return BaseUrl;
                }
            }
            catch (OperationCanceledException)
            {
                ct.ThrowIfCancellationRequested();
            }
            catch (HttpRequestException)
            {
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
        }

        _process.Dispose();
        _process = null;
    }
    static string? OpencodeExecutable => field ??= FindExecutable("opencode");
    static string? FindExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return null;

        foreach (var directory in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;

            var candidate = Path.Combine(directory, name);

            if (OperatingSystem.IsWindows())
            {
                if (!Path.HasExtension(candidate))
                {
                    var exe = candidate + ".exe";
                    if (File.Exists(exe))
                        return Path.GetFullPath(exe);
                }
            }

            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    static Lazy<Task<OpencodeExecutableStatus>> executableStatus => field ??= new(GetExecutableStatusPrivate);
    public static Task<OpencodeExecutableStatus> GetExecutableStatus() => executableStatus.Value;
    public static Version RequiredOpencodeVersion => field ??= Version.Parse("1.18.0");
    static async Task<OpencodeExecutableStatus> GetExecutableStatusPrivate()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OpencodeExecutable,
            Arguments = $"--version",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        var process = Process.Start(startInfo);
        if (process is null)
            return OpencodeExecutableStatus.NotAvaliable;
        await process.WaitForExitAsync();
        if (process.ExitCode is not 0)
            return OpencodeExecutableStatus.NotAvaliable;
        var version = await process.StandardOutput.ReadToEndAsync();
        if (Version.TryParse(version, out var v))
        {
            if (v >= RequiredOpencodeVersion)
                return OpencodeExecutableStatus.Avaliable;
            else
                return OpencodeExecutableStatus.MayNeedUpgrade;
        }
        return OpencodeExecutableStatus.NotAvaliable;
    } 
}

public enum OpencodeExecutableStatus
{
    NotAvaliable,
    MayNeedUpgrade,
    Avaliable
}