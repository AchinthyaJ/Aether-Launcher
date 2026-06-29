using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace OfflineMinecraftLauncher.Platform.Windows;

/// <summary>
/// Windows-specific performance optimizations for the Aether Launcher.
/// These address Avalonia rendering quirks on Windows that don't exist on Linux.
/// </summary>
internal static class WindowsOptimizations
{
    /// <summary>
    /// Apply all Windows-specific optimizations at startup.
    /// Call this early in the application lifecycle (before window creation).
    /// </summary>
    public static void Apply()
    {
        if (!OperatingSystem.IsWindows()) return;

        // 1. Set process DPI awareness for crisp rendering on high-DPI displays
        SetDpiAwareness();

        // 2. Set process priority to above-normal for smoother UI
        SetProcessPriority();

        // 3. Reduce timer resolution for smoother animations
        SetTimerResolution();
    }

    /// <summary>
    /// Apply rendering optimizations to an image control used for skin preview.
    /// On Windows, we use LowQuality interpolation to avoid expensive bicubic sampling.
    /// </summary>
    public static void OptimizeSkinPreviewImage(Avalonia.Controls.Image image)
    {
        if (!OperatingSystem.IsWindows()) return;
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.LowQuality);
    }

    /// <summary>
    /// Get the optimal preview timer interval based on window focus state.
    /// When the window is not focused, we reduce the FPS to save CPU.
    /// </summary>
    public static int GetAdaptiveTimerInterval(bool isWindowFocused, bool isPerformanceMode)
    {
        if (isPerformanceMode) return 33; // 30 FPS in performance mode
        if (!isWindowFocused && OperatingSystem.IsWindows()) return 100; // 10 FPS when unfocused on Windows
        return 16; // 60 FPS normal
    }

    private static void SetDpiAwareness()
    {
        try
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 15063))
            {
                SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            }
        }
        catch
        {
            // Silently fail — Avalonia handles DPI scaling as fallback
        }
    }

    private static void SetProcessPriority()
    {
        try
        {
            System.Diagnostics.Process.GetCurrentProcess().PriorityClass = 
                System.Diagnostics.ProcessPriorityClass.AboveNormal;
        }
        catch
        {
            // May fail without admin privileges — acceptable
        }
    }

    private static void SetTimerResolution()
    {
        try
        {
            // Request 1ms timer resolution for smoother animations
            timeBeginPeriod(1);
        }
        catch
        {
            // Non-critical optimization
        }
    }

    // Windows API imports
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint uMilliseconds);
}
