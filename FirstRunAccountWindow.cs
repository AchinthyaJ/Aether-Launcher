using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media.Transformation;
using Avalonia.Controls.Primitives;

namespace OfflineMinecraftLauncher;

public sealed class FirstRunAccountWindow : Window
{
    private readonly UserSettingsStore _settingsStore;
    private readonly UserSettings _settings;
    private readonly MinecraftAuthenticationService _authService = new();

    private enum AccountMode
    {
        Offline,
        Microsoft
    }

    private AccountMode _mode = AccountMode.Offline;

    private readonly TextBox _usernameInput;
    private readonly Button _submitButton;
    
    private readonly Button _offlineTabBtn;
    private readonly Button _microsoftTabBtn;
    private readonly StackPanel _offlinePanel;
    private readonly StackPanel _microsoftPanel;

    private readonly IBrush _activeTabBrush;
    private readonly IBrush _inactiveTextBrush;
    private readonly IBrush _offlineBtnBrush;
    private readonly IBrush _microsoftBtnBrush;

    public FirstRunAccountWindow()
    {
        Title = "Aether Launcher - Welcome";
        Width = 840;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SystemDecorations = SystemDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        TransparencyLevelHint = new[]
        {
            WindowTransparencyLevel.Mica,
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Transparent
        };
        Background = Brushes.Transparent;

        // Initialize Settings
        var initialPath = new MinecraftPath();
        initialPath.CreateDirs();
        _settingsStore = new UserSettingsStore(initialPath.BasePath);
        _settings = _settingsStore.Load();

        // Drag window interaction on background
        this.PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                this.BeginMoveDrag(e);
            }
        };

        // Theme brushes
        _activeTabBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#5972FF"), 0),
                new GradientStop(Color.Parse("#59D6FF"), 1)
            }
        };
        _inactiveTextBrush = new SolidColorBrush(Color.Parse("#64748B"));

        _offlineBtnBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#6E5BFF"), 0),
                new GradientStop(Color.Parse("#00F2FE"), 1)
            }
        };

        _microsoftBtnBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#2563EB"), 0),
                new GradientStop(Color.Parse("#3B82F6"), 1)
            }
        };

        // UI Controls Init
        _usernameInput = new TextBox
        {
            Watermark = "Minecraft Username",
            Background = new SolidColorBrush(Color.FromArgb(100, 7, 10, 18)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 92, 115, 166)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 14),
            FontSize = 14,
            FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
        };

        // Set initial username if applicable
        if (_settings.IsFirstRun && string.IsNullOrWhiteSpace(_settings.SelectedAccountId) && _settings.Accounts.Count == 0)
        {
            _usernameInput.Text = string.IsNullOrWhiteSpace(_settings.Username) ? Environment.UserName : _settings.Username;
        }

        // Add a glow focus visual effect
        _usernameInput.GotFocus += (s, e) =>
        {
            _usernameInput.BorderBrush = new SolidColorBrush(Color.Parse("#6E5BFF"));
        };
        _usernameInput.LostFocus += (s, e) =>
        {
            _usernameInput.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 92, 115, 166));
        };

        _submitButton = new Button
        {
            Content = "Start Playing",
            Background = _offlineBtnBrush,
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(20, 14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Height = 50,
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
        };
        _submitButton.Click += async (_, _) => await SubmitAsync();

        ApplyHoverMotion(_submitButton);

        // Segmented Control Tab Buttons
        _offlineTabBtn = new Button
        {
            Content = "Offline Account",
            CornerRadius = new CornerRadius(20),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
        };
        _offlineTabBtn.Click += (_, _) =>
        {
            _mode = AccountMode.Offline;
            SyncModeUi();
        };
        ApplyHoverMotion(_offlineTabBtn);

        _microsoftTabBtn = new Button
        {
            Content = "Microsoft Mode",
            CornerRadius = new CornerRadius(20),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
        };
        _microsoftTabBtn.Click += (_, _) =>
        {
            _mode = AccountMode.Microsoft;
            SyncModeUi();
        };
        ApplyHoverMotion(_microsoftTabBtn);

        // ----------------- LEFT PANEL CONTENT -----------------
        
        // Custom Rotating Logo
        var logoContainer = new Grid
        {
            Width = 64,
            Height = 64,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        
        var logoBgGlow = new Border
        {
            CornerRadius = new CornerRadius(32),
            Background = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(100, 110, 91, 255), 0),
                    new GradientStop(Color.FromArgb(0, 110, 91, 255), 1)
                }
            }
        };
        logoContainer.Children.Add(logoBgGlow);

        var logoBorder = new Border
        {
            Width = 80,
            Height = 80,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = new Image
            {
                Width = 60,
                Height = 60,
                Source = new Bitmap(AssetLoader.Open(new Uri((Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light) ? "avares://AetherLauncher/assets/deathclient-taskbar-light.png" : "avares://AetherLauncher/assets/deathclient-taskbar.png"))),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        logoContainer.Children.Add(logoBorder);

        var brandTitle = new TextBlock
        {
            Text = "AETHER CLIENT",
            FontSize = 22,
            FontWeight = FontWeight.ExtraBold,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
        };

        var brandSubtitle = new TextBlock
        {
            Text = "PERFORMANCE REDEFINED",
            FontSize = 9,
            FontWeight = FontWeight.Black,
            Foreground = new SolidColorBrush(Color.Parse("#72C8FF")),
            FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
        };

        var brandHeader = new StackPanel
        {
            Spacing = 4,
            Children = { brandTitle, brandSubtitle }
        };

        var featuresStack = new StackPanel
        {
            Spacing = 16
        };
        featuresStack.Children.Add(CreateFeatureItem("60FPS+ Optimizations"));
        featuresStack.Children.Add(CreateFeatureItem("Customizable to any level"));
        featuresStack.Children.Add(CreateFeatureItem("Multi-Account Manager"));
        featuresStack.Children.Add(CreateFeatureItem("Modern UI Experience"));

        var leftStack = new StackPanel
        {
            Spacing = 30,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { logoContainer, brandHeader, featuresStack }
        };

        var leftPane = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(160, 6, 9, 18)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(36),
            Child = leftStack
        };
        Grid.SetColumn(leftPane, 0);

        // ----------------- RIGHT PANEL CONTENT -----------------

        var welcomeTitle = new TextBlock
        {
            Text = "Welcome Aboard",
            FontSize = 26,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
        };

        var welcomeSub = new TextBlock
        {
            Text = "Choose how you'd like to authenticate to begin.",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
            Margin = new Thickness(0, 4, 0, 20),
            FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
        };

        // Pill segmented tab selector container
        Grid.SetColumn(_offlineTabBtn, 0);
        Grid.SetColumn(_microsoftTabBtn, 1);

        var pillSelector = new Border
        {
            Height = 44,
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(Color.FromArgb(200, 6, 9, 18)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(50, 110, 91, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                Children =
                {
                    _offlineTabBtn,
                    _microsoftTabBtn
                }
            }
        };

        // Offline Content Panel
        _offlinePanel = new StackPanel
        {
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = "Offline Username",
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
                    FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
                },
                _usernameInput,
                new TextBlock
                {
                    Text = "Allows playing locally under any nickname. Ideal for offline sessions. (Note: standard multiplayer servers enforce online Microsoft authentication.)",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.Parse("#64748B")),
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
                }
            }
        };

        // Microsoft Content Panel
        var msRed = new Border { Background = new SolidColorBrush(Color.Parse("#F25022")), Margin = new Thickness(0, 0, 1, 1) };
        var msGreen = new Border { Background = new SolidColorBrush(Color.Parse("#7FBA00")), Margin = new Thickness(1, 0, 0, 1) };
        var msBlue = new Border { Background = new SolidColorBrush(Color.Parse("#00A1F1")), Margin = new Thickness(0, 1, 1, 0) };
        var msYellow = new Border { Background = new SolidColorBrush(Color.Parse("#FFB900")), Margin = new Thickness(1, 1, 0, 0) };

        Grid.SetRow(msRed, 0); Grid.SetColumn(msRed, 0);
        Grid.SetRow(msGreen, 0); Grid.SetColumn(msGreen, 1);
        Grid.SetRow(msBlue, 1); Grid.SetColumn(msBlue, 0);
        Grid.SetRow(msYellow, 1); Grid.SetColumn(msYellow, 1);

        var msLogo = new Grid
        {
            Width = 16,
            Height = 16,
            Margin = new Thickness(0, 0, 10, 0),
            RowDefinitions = new RowDefinitions("*,*"),
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            Children = { msRed, msGreen, msBlue, msYellow }
        };

        _microsoftPanel = new StackPanel
        {
            Spacing = 16,
            Children =
            {
                new Border
                {
                    Padding = new Thickness(16, 20),
                    Background = new SolidColorBrush(Color.FromArgb(70, 110, 91, 255)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(40, 110, 91, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(14),
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "🔒",
                                FontSize = 22,
                                VerticalAlignment = VerticalAlignment.Center
                            },
                            new StackPanel
                            {
                                Spacing = 2,
                                Children =
                                {
                                    new TextBlock
                                    {
                                        Text = "Secure Browser Authentication",
                                        FontSize = 13,
                                        FontWeight = FontWeight.Bold,
                                        Foreground = Brushes.White,
                                        FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
                                    },
                                    new TextBlock
                                    {
                                        Text = "A secure browser window will open for official login.",
                                        FontSize = 11,
                                        Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
                                        TextWrapping = TextWrapping.Wrap,
                                        FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
                                    }
                                }
                            }
                        }
                    }
                },
                new TextBlock
                {
                    Text = "Supported features: Official online multiplayer, skins synchronization, Realms access, and full Microsoft account security guarantees.",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.Parse("#64748B")),
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
                }
            }
        };

        // Window Controls (top right)
        var minBtn = new Button
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
            BorderThickness = new Thickness(0),
            Content = "−",
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Padding = new Thickness(0)
        };
        ApplyHoverMotion(minBtn);
        minBtn.Click += (_, _) => WindowState = WindowState.Minimized;

        var closeBtn = new Button
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
            BorderThickness = new Thickness(0),
            Content = "✕",
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            Padding = new Thickness(0)
        };
        ApplyHoverMotion(closeBtn);
        closeBtn.Click += (_, _) => Close();

        var titleBarControls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { minBtn, closeBtn }
        };

        var activePanelGrid = new Grid
        {
            Children = { _offlinePanel, _microsoftPanel }
        };

        var headerStack = new StackPanel { Children = { welcomeTitle, welcomeSub } };
        var activePanelBorder = new Border { Margin = new Thickness(0, 24, 0, 0), Child = activePanelGrid };

        Grid.SetRow(headerStack, 0);
        Grid.SetRow(pillSelector, 1);
        Grid.SetRow(activePanelBorder, 2);
        Grid.SetRow(_submitButton, 3);

        var rightStack = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Children =
            {
                headerStack,
                pillSelector,
                activePanelBorder,
                _submitButton
            }
        };

        var rightPane = new Border
        {
            Padding = new Thickness(36),
            Child = new Grid
            {
                Children =
                {
                    rightStack,
                    titleBarControls
                }
            }
        };
        Grid.SetColumn(rightPane, 1);

        // ----------------- ROOT COMPOSITION -----------------

        // Overlay glowing backgrounds (Nebulas)
        var backgroundCanvas = new Canvas
        {
            IsHitTestVisible = false,
            Children =
            {
                new Border
                {
                    Width = 500,
                    Height = 500,
                    CornerRadius = new CornerRadius(250),
                    Background = new RadialGradientBrush
                    {
                        Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                        GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                        RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
                        RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
                        GradientStops =
                        {
                            new GradientStop(Color.FromArgb(28, 110, 91, 255), 0),
                            new GradientStop(Color.FromArgb(0, 110, 91, 255), 1)
                        }
                    },
                    [Canvas.LeftProperty] = -150d,
                    [Canvas.TopProperty] = -150d
                },
                new Border
                {
                    Width = 450,
                    Height = 450,
                    CornerRadius = new CornerRadius(225),
                    Background = new RadialGradientBrush
                    {
                        Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                        GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                        RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
                        RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
                        GradientStops =
                        {
                            new GradientStop(Color.FromArgb(22, 0, 242, 254), 0),
                            new GradientStop(Color.FromArgb(0, 0, 242, 254), 1)
                        }
                    },
                    [Canvas.RightProperty] = -120d,
                    [Canvas.BottomProperty] = -120d
                },
                new Border
                {
                    Width = 350,
                    Height = 350,
                    CornerRadius = new CornerRadius(175),
                    Background = new RadialGradientBrush
                    {
                        Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                        GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                        RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
                        RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
                        GradientStops =
                        {
                            new GradientStop(Color.FromArgb(20, 211, 0, 197), 0),
                            new GradientStop(Color.FromArgb(0, 211, 0, 197), 1)
                        }
                    },
                    [Canvas.LeftProperty] = -50d,
                    [Canvas.BottomProperty] = -100d
                }
            }
        };

        var mainLayout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("330,*"),
            Children =
            {
                backgroundCanvas,
                leftPane,
                rightPane
            }
        };
        Grid.SetColumnSpan(backgroundCanvas, 2);

        Content = new Border
        {
            CornerRadius = new CornerRadius(24),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.Parse("#03050C")),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 110, 91, 255)),
            BorderThickness = new Thickness(1),
            Child = mainLayout
        };

        SyncModeUi();
    }

    private Control CreateFeatureItem(string text)
    {
        var checkIcon = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#22C55E"), 0),
                    new GradientStop(Color.Parse("#10B981"), 1)
                }
            },
            Child = new TextBlock
            {
                Text = "✓",
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
            }
        };

        var label = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.Parse("#94A3B8")),
            FontSize = 13,
            FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("SF Pro, Inter, Segoe UI")
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { checkIcon, label }
        };
    }

    private void SyncModeUi()
    {
        if (_mode == AccountMode.Offline)
        {
            _offlineTabBtn.Background = _activeTabBrush;
            _offlineTabBtn.Foreground = Brushes.White;
            _microsoftTabBtn.Background = Brushes.Transparent;
            _microsoftTabBtn.Foreground = _inactiveTextBrush;

            _offlinePanel.IsVisible = true;
            _microsoftPanel.IsVisible = false;

            _submitButton.Content = "Start Playing";
            _submitButton.Background = _offlineBtnBrush;
        }
        else
        {
            _microsoftTabBtn.Background = _activeTabBrush;
            _microsoftTabBtn.Foreground = Brushes.White;
            _offlineTabBtn.Background = Brushes.Transparent;
            _offlineTabBtn.Foreground = _inactiveTextBrush;

            _offlinePanel.IsVisible = false;
            _microsoftPanel.IsVisible = true;

            _submitButton.Content = "Continue to Browser";
            _submitButton.Background = _microsoftBtnBrush;
        }
    }

    private async Task SubmitAsync()
    {
        _submitButton.IsEnabled = false;
        _offlineTabBtn.IsEnabled = false;
        _microsoftTabBtn.IsEnabled = false;
        _usernameInput.IsEnabled = false;

        try
        {
            if (_mode == AccountMode.Offline)
            {
                var username = (_usernameInput.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(username))
                {
                    await DialogService.ShowInfoAsync(this, "Username required", "Enter a username.");
                    return;
                }

                var acc = new LauncherAccount
                {
                    Provider = "offline",
                    Username = username,
                    DisplayName = username
                };

                _settings.Accounts.Add(acc);
                _settings.SelectedAccountId = acc.Id;
                _settings.Username = username;
                _settings.OfflineMode = true;
                _settings.IsFirstRun = false;
                _settingsStore.Save(_settings);

                OpenMainWindowAndClose();
                return;
            }

            var clientId = string.IsNullOrWhiteSpace(_settings.MicrosoftClientId) ? "00000000402b5328" : _settings.MicrosoftClientId;
            using var cts = new CancellationTokenSource();

            var session = await _authService.BeginDeviceLoginAsync(clientId, cts.Token);
            Process.Start(new ProcessStartInfo { FileName = session.VerificationUri, UseShellExecute = true });

            var dialogTask = DialogService.ShowMicrosoftAuthDialogAsync(this, session.UserCode, session.VerificationUri, cts);
            var pollTask = _authService.CompleteDeviceLoginAsync(clientId, session, cts.Token);

            var completed = await Task.WhenAny(dialogTask, pollTask);
            if (completed != pollTask)
            {
                // User cancelled
                return;
            }

            var account = await pollTask;
            var existing = _settings.Accounts.Find(a => a.Provider == "microsoft" && a.Uuid == account.Uuid);
            if (existing != null) _settings.Accounts.Remove(existing);

            _settings.Accounts.Add(account);
            _settings.SelectedAccountId = account.Id;
            _settings.Username = account.Username;
            _settings.OfflineMode = false;
            _settings.IsFirstRun = false;
            _settingsStore.Save(_settings);

            OpenMainWindowAndClose();
        }
        catch (Exception ex)
        {
            await DialogService.ShowInfoAsync(this, "Account setup failed", ex.Message);
        }
        finally
        {
            _submitButton.IsEnabled = true;
            _offlineTabBtn.IsEnabled = true;
            _microsoftTabBtn.IsEnabled = true;
            _usernameInput.IsEnabled = true;
        }
    }

    private void OpenMainWindowAndClose()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var main = new MainWindow();
            desktop.MainWindow = main;
            main.Show();
            Close();
            return;
        }

        // Fallback
        new MainWindow().Show();
        Close();
    }
    private void ApplyHoverMotion(Control? control)
    {
        if (control == null) return;
        control.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        
        var transitions = new Transitions
        {
            new DoubleTransition { Property = Control.OpacityProperty, Easing = new CubicEaseOut(), Duration = TimeSpan.FromMilliseconds(250) },
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Easing = new BackEaseOut(), Duration = TimeSpan.FromMilliseconds(300) }
        };

        if (control is TemplatedControl)
        {
            transitions.Add(new BrushTransition { Property = TemplatedControl.BackgroundProperty, Easing = new CubicEaseOut(), Duration = TimeSpan.FromMilliseconds(250) });
            transitions.Add(new BrushTransition { Property = TemplatedControl.ForegroundProperty, Easing = new CubicEaseOut(), Duration = TimeSpan.FromMilliseconds(200) });
            transitions.Add(new BrushTransition { Property = TemplatedControl.BorderBrushProperty, Easing = new CubicEaseOut(), Duration = TimeSpan.FromMilliseconds(250) });
        }
        else if (control is Border)
        {
            transitions.Add(new BrushTransition { Property = Border.BackgroundProperty, Easing = new CubicEaseOut(), Duration = TimeSpan.FromMilliseconds(250) });
            transitions.Add(new BrushTransition { Property = Border.BorderBrushProperty, Easing = new CubicEaseOut(), Duration = TimeSpan.FromMilliseconds(250) });
            transitions.Add(new BoxShadowsTransition { Property = Border.BoxShadowProperty, Easing = new CubicEaseOut(), Duration = TimeSpan.FromMilliseconds(250) });
        }

        control.Transitions = transitions;
        
        IBrush? originalBg = null;
        IBrush? originalFg = null;
        IBrush? originalBorder = null;
        Thickness originalBorderThickness = new Thickness(0);
        bool captured = false;
        
        control.PointerEntered += (s, e) =>
        {
            control.Opacity = 0.95;
            control.RenderTransform = TransformOperations.Parse("scale(1.07) rotate(1.5deg) translate(0px, -3px)");
            
            if (control is Button btn)
            {
                if (!captured)
                {
                    originalBg = btn.Background;
                    originalFg = btn.Foreground;
                    originalBorder = btn.BorderBrush;
                    originalBorderThickness = btn.BorderThickness;
                    captured = true;
                }
                
                if (btn == _submitButton)
                {
                    btn.BorderBrush = new SolidColorBrush(Color.Parse("#FFFFFF"));
                    btn.BorderThickness = new Thickness(1.5);
                }
                else if (btn.Content?.ToString() == "−")
                {
                    btn.Background = new SolidColorBrush(Color.FromArgb(60, 245, 158, 11));
                    btn.Foreground = Brushes.White;
                }
                else if (btn.Content?.ToString() == "✕")
                {
                    btn.Background = new SolidColorBrush(Color.Parse("#EF4444"));
                    btn.Foreground = Brushes.White;
                }
            }
        };
        control.PointerExited += (s, e) =>
        {
            control.Opacity = 1.0;
            control.RenderTransform = TransformOperations.Parse("scale(1.0) rotate(0deg) translate(0px, 0px)");
            if (captured && control is Button btn)
            {
                if (btn == _submitButton)
                {
                    btn.Background = _mode == AccountMode.Offline ? _offlineBtnBrush : _microsoftBtnBrush;
                    btn.BorderBrush = originalBorder;
                    btn.BorderThickness = originalBorderThickness;
                }
                else
                {
                    btn.Background = originalBg;
                    btn.Foreground = originalFg;
                    btn.BorderBrush = originalBorder;
                    btn.BorderThickness = originalBorderThickness;
                }
            }
        };
    }
}
