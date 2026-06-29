using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CmlLib.Core;

namespace OfflineMinecraftLauncher;

public class SplashWindow : Window
{
    public SplashWindow()
    {
        Title = "Aether Launcher";
        Width = 480;
        Height = 300;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[]
        {
            WindowTransparencyLevel.Mica,
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Transparent
        };

        // Start fully transparent — animation runs in Opened
        Opacity = 0;

        var titleText = new TextBlock
        {
            Text = "A E T H E R",
            FontSize = 42,
            FontWeight = FontWeight.Black,
            Foreground = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#00F2FE"), 0),
                    new GradientStop(Color.Parse("#6E5BFF"), 1)
                }
            },
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6)
        };

        var subtitleText = new TextBlock
        {
            Text = "PROJECT CLIENT LAUNCHER",
            FontSize = 10,
            FontWeight = FontWeight.Light,
            LetterSpacing = 5,
            Foreground = new SolidColorBrush(Color.Parse("#8E96A3")),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var content = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 0,
            Children = { titleText, subtitleText }
        };

        Content = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#0D111A"), 0),
                    new GradientStop(Color.Parse("#161924"), 1)
                }
            },
            BorderBrush = new SolidColorBrush(Color.Parse("#2A2D3D")),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(16),
            BoxShadow = BoxShadows.Parse("0 12 36 0 #7F000000, 0 0 30 0 #1A00F2FE"),
            Padding = new Thickness(32),
            Child = content
        };

        // All sequencing happens here — after the window is actually on screen
        Opened += async (_, _) =>
        {
            // Kick off skin server in background immediately
            _ = Task.Run(async () =>
            {
                try { await AppRuntime.SkinServer.StartAsync(); }
                catch (Exception ex) { LauncherLog.Error("Failed to initialize node skin server.", ex); }
            });

            // Allow one layout/render pass before animating
            await Task.Delay(30);

            // ── Fade IN (200ms) ──────────────────────────────────────────────
            Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Window.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(200),
                    Easing = new CubicEaseOut()
                }
            };
            Opacity = 1.0;

            // Stay visible for ~650ms (fade-in takes 200ms of this slot)
            await Task.Delay(650);

            // ── Fade OUT (150ms, quick) ──────────────────────────────────────
            Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Window.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(150),
                    Easing = new CubicEaseIn()
                }
            };
            Opacity = 0.0;

            await Task.Delay(160); // wait for fade-out to complete

            // ── Navigate ────────────────────────────────────────────────────
            Dispatcher.UIThread.Post(() =>
            {
                bool needsOnboarding = true;
                try
                {
                    var initialPath = new MinecraftPath();
                    initialPath.CreateDirs();
                    var store = new UserSettingsStore(initialPath.BasePath);
                    var settings = store.Load();
                    needsOnboarding = settings.IsFirstRun || settings.Accounts.Count == 0;
                }
                catch { }

                var nextWindow = needsOnboarding ? (Window)new FirstRunAccountWindow() : new MainWindow();

                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.MainWindow = nextWindow;

                nextWindow.Show();
                Close();
            });
        };
    }
}
