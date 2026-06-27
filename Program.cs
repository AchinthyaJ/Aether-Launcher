using Avalonia;
using System;
using System.Threading.Tasks;

namespace OfflineMinecraftLauncher;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
                LauncherLog.Error("Unhandled application exception.", exception);
            else
                LauncherLog.Error($"Unhandled application exception: {eventArgs.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            LauncherLog.Error("Unobserved task exception.", eventArgs.Exception);
            eventArgs.SetObserved();
        };

        // Apply Windows-specific optimizations before Avalonia startup
        if (OperatingSystem.IsWindows())
        {
            try { Platform.Windows.WindowsOptimizations.Apply(); }
            catch { /* Non-critical — continue startup */ }
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect();
    }
}
