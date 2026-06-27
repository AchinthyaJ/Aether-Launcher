using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CmlLib.Core;

namespace OfflineMinecraftLauncher;

public class SplashWindow : Window
{
    private readonly ProgressBar _progressBar;
    private readonly TextBlock _statusText;

    public SplashWindow()
    {
        Title = "Aether Launcher";
        Width = 480;
        Height = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[]
        {
            WindowTransparencyLevel.Mica,
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Transparent
        };

        _progressBar = new ProgressBar
        {
            Width = 360,
            Height = 6,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.Parse("#1A1F2C")),
            Foreground = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#00F2FE"), 0),
                    new GradientStop(Color.Parse("#6E5BFF"), 1)
                }
            },
            Margin = new Thickness(0, 40, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        _statusText = new TextBlock
        {
            Text = "Starting Aether Launcher...",
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
            HorizontalAlignment = HorizontalAlignment.Center,
            FontWeight = FontWeight.Medium
        };

        var titleText = new TextBlock
        {
            Text = "A E T H E R",
            FontSize = 38,
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
            Margin = new Thickness(0, 10, 0, 2)
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
            Children = { titleText, subtitleText, _progressBar, _statusText }
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

        _ = StartLoadingSequenceAsync();
    }

    private async Task StartLoadingSequenceAsync()
    {
        var steps = new (string text, int targetValue)[]
        {
            ("Initializing game directories...", 15),
            ("Loading user configurations...", 35),
            ("Starting local skin server...", 60),
            ("Caching launcher assets...", 85),
            ("Opening workspace...", 100)
        };

        foreach (var step in steps)
        {
            int current = (int)_progressBar.Value;
            int diff = step.targetValue - current;

            Dispatcher.UIThread.Post(() => _statusText.Text = step.text);

            if (step.targetValue == 60)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await AppRuntime.SkinServer.StartAsync();
                    }
                    catch (Exception ex)
                    {
                        LauncherLog.Error("Failed to initialize node skin server.", ex);
                    }
                });
            }

            for (int i = 0; i <= diff; i++)
            {
                Dispatcher.UIThread.Post(() => _progressBar.Value = current + i);
                await Task.Delay(12);
            }

            await Task.Delay(250);
        }

        await Task.Delay(200);

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
            catch {}

            var nextWindow = needsOnboarding ? (Window)new FirstRunAccountWindow() : new MainWindow();
            
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = nextWindow;
            }

            nextWindow.Show();
            Close();
        });
    }
}
