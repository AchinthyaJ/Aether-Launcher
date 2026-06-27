using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace OfflineMinecraftLauncher;

internal sealed class NodeSkinServerManager : IDisposable
{
    private static readonly Uri BaseUri = new("http://127.0.0.1:47135/");
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    private readonly string _storageDirectory;
    private Process? _process;
    private bool _ownsProcess;

    public NodeSkinServerManager()
    {
        _storageDirectory = Path.Combine(AppRuntime.DataDirectory, "skin-server");
        Directory.CreateDirectory(_storageDirectory);
    }

    public Uri ServerBaseUri => BaseUri;

    private static async Task<bool> IsPortOccupiedAsync(int port)
    {
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            var connectTask = tcp.ConnectAsync("127.0.0.1", port);
            var completedTask = await Task.WhenAny(connectTask, Task.Delay(200));
            if (completedTask == connectTask)
            {
                await connectTask;
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var portOccupied = await IsPortOccupiedAsync(47135);
        if (portOccupied)
        {
            LauncherLog.Info("Skin server port 47135 is occupied. Attempting to shutdown old instance...");
            try
            {
                using var response = await HttpClient.PostAsync(new Uri(BaseUri, "shutdown"), null, cancellationToken);
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"Failed to send shutdown command to old skin server instance: {ex.Message}");
            }

            for (int i = 0; i < 15; i++)
            {
                await Task.Delay(100, cancellationToken);
                if (!await IsPortOccupiedAsync(47135))
                {
                    portOccupied = false;
                    break;
                }
            }

            if (portOccupied)
            {
                LauncherLog.Warn("Port 47135 is still occupied. Force-killing process on port 47135...");
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        // Windows: use netstat to find PID on port, then taskkill
                        var findPidInfo = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = "/c \"for /f \"tokens=5\" %a in ('netstat -ano ^| findstr :47135 ^| findstr LISTENING') do taskkill /F /PID %a\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        using var killProc = Process.Start(findPidInfo);
                        if (killProc != null)
                        {
                            await killProc.WaitForExitAsync(cancellationToken);
                        }
                    }
                    else
                    {
                        // Linux/macOS: use fuser
                        var killInfo = new ProcessStartInfo
                        {
                            FileName = "fuser",
                            Arguments = "-k 47135/tcp",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var killProc = Process.Start(killInfo);
                        if (killProc != null)
                        {
                            await killProc.WaitForExitAsync(cancellationToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LauncherLog.Error($"Failed to force-kill process on port 47135: {ex.Message}");
                }
                
                await Task.Delay(500, cancellationToken);
            }
        }

        if (_process is { HasExited: false })
            return;

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "node-skin-server", "server.js");
        if (!File.Exists(scriptPath))
        {
            LauncherLog.Warn($"Node skin server script not found at '{scriptPath}'.");
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "node",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add("47135");
        startInfo.ArgumentList.Add("--storage");
        startInfo.ArgumentList.Add(_storageDirectory);

        try
        {
            _process = Process.Start(startInfo);
            _ownsProcess = _process is not null;
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to start node skin server process.", ex);
            return;
        }

        if (_process is null)
            return;

        _ = Task.Run(() => DrainAsync(_process.StandardOutput, cancellationToken), cancellationToken);
        _ = Task.Run(() => DrainAsync(_process.StandardError, cancellationToken), cancellationToken);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (await IsHealthyAsync(cancellationToken))
                return;

            await Task.Delay(250, cancellationToken);
        }

        LauncherLog.Warn("Node skin server did not become healthy in time.");
    }

    public void Dispose()
    {
        if (!_ownsProcess || _process is null)
            return;

        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Ignore shutdown failures during app exit.
        }
        finally
        {
            _process.Dispose();
            _process = null;
            _ownsProcess = false;
        }
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(line))
                    LauncherLog.Info($"[SkinServer] {line}");
            }
        }
        catch
        {
            // Ignore stream closure during shutdown.
        }
    }

    private static async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await HttpClient.GetAsync(new Uri(BaseUri, "health"), cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
