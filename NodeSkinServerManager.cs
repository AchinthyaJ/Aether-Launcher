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

        var nodeExe = ResolveNodeExecutable();

        var startInfo = new ProcessStartInfo
        {
            FileName = nodeExe,
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

    /// <summary>
    /// Resolves the Node.js executable path.
    /// Priority: (1) bundled node.exe next to the launcher, (2) found via where/which, (3) common Windows install paths, (4) bare "node" fallback.
    /// </summary>
    private static string ResolveNodeExecutable()
    {
        if (OperatingSystem.IsWindows())
        {
            // 1. Bundled node.exe in the app directory
            var bundled = Path.Combine(AppContext.BaseDirectory, "node.exe");
            if (File.Exists(bundled))
            {
                LauncherLog.Info($"[NodeServer] Using bundled node.exe: {bundled}");
                return bundled;
            }

            // 2. Bundled inside node-skin-server\node.exe
            var bundledInServer = Path.Combine(AppContext.BaseDirectory, "node-skin-server", "node.exe");
            if (File.Exists(bundledInServer))
            {
                LauncherLog.Info($"[NodeServer] Using bundled node.exe (server dir): {bundledInServer}");
                return bundledInServer;
            }

            // 3. Use 'where.exe' to search PATH
            try
            {
                using var whereProc = Process.Start(new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = "node",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                });
                if (whereProc != null)
                {
                    var result = whereProc.StandardOutput.ReadLine()?.Trim();
                    whereProc.WaitForExit();
                    if (!string.IsNullOrEmpty(result) && File.Exists(result))
                    {
                        LauncherLog.Info($"[NodeServer] Found node.exe via where.exe: {result}");
                        return result;
                    }
                }
            }
            catch { /* where.exe not found or node not on PATH */ }

            // 4. Common Windows install locations
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming", "nvm", "current", "node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nvm", "current", "node.exe"),
                @"C:\Program Files\nodejs\node.exe",
                @"C:\Program Files (x86)\nodejs\node.exe"
            };
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    LauncherLog.Info($"[NodeServer] Found node.exe at known location: {candidate}");
                    return candidate;
                }
            }

            LauncherLog.Warn("[NodeServer] node.exe not found in bundled dir, PATH, or common locations. Falling back to 'node'. Please install Node.js.");
            return "node.exe";
        }
        else
        {
            // Linux/macOS: try bundled, then 'node', then 'nodejs' (Debian/Ubuntu uses 'nodejs')
            var bundled = Path.Combine(AppContext.BaseDirectory, "node-skin-server", "node");
            if (File.Exists(bundled))
                return bundled;

            try
            {
                using var which = Process.Start(new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = "node",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                });
                if (which != null)
                {
                    var result = which.StandardOutput.ReadLine()?.Trim();
                    which.WaitForExit();
                    if (!string.IsNullOrEmpty(result) && File.Exists(result))
                        return result;
                }
            }
            catch { }

            // Fallback to nodejs (Debian/Ubuntu)
            try
            {
                using var which2 = Process.Start(new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = "nodejs",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                });
                if (which2 != null)
                {
                    var result = which2.StandardOutput.ReadLine()?.Trim();
                    which2.WaitForExit();
                    if (!string.IsNullOrEmpty(result) && File.Exists(result))
                        return result;
                }
            }
            catch { }

            return "node";
        }
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
