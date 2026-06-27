using System;
using System.IO;
using System.Text;

namespace OfflineMinecraftLauncher;

internal static class LauncherLog
{
    private static readonly object Sync = new();
    public static event Action<string>? OnLog;
    private static readonly string BaseDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".death-client");
    private static readonly string LogDirectory = Path.Combine(BaseDirectory, "logs");
    private static readonly string LogPath = Path.Combine(LogDirectory, "latest.log");
    private static readonly string PreviousLogPath = Path.Combine(LogDirectory, "previous.log");

    static LauncherLog()
    {
        Directory.CreateDirectory(LogDirectory);
        // Rotate: move current latest.log to previous.log on startup
        try
        {
            if (File.Exists(LogPath))
            {
                File.Copy(LogPath, PreviousLogPath, overwrite: true);
                File.Delete(LogPath);
            }
        }
        catch { /* Rotation must never crash the launcher */ }
    }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message) => Write("WARN", message, null);
    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    public static void AtomicWriteAllText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException($"Cannot determine directory for '{path}'.");

        Directory.CreateDirectory(directory);
        var tempPath = $"{path}.tmp";
        File.WriteAllText(tempPath, content, Encoding.UTF8);
        File.Move(tempPath, path, true);
    }

    private static void Write(string level, string message, Exception? exception)
    {
        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {level} {message}";
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                using var writer = new StreamWriter(LogPath, append: true, Encoding.UTF8);
                writer.WriteLine(line);
                if (exception is not null)
                    writer.WriteLine(exception);
            }
            catch
            {
                // Logging must never crash the launcher.
            }
        }

        // Write logs to the console for immediate visibility
        Console.WriteLine(line);
        if (exception is not null)
            Console.WriteLine(exception);

        OnLog?.Invoke(line);
    }
}
