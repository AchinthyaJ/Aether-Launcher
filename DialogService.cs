using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Animation;
using System.Diagnostics;
using System.Threading;

namespace OfflineMinecraftLauncher;

internal static class DialogService
{
    public static async Task ShowInfoAsync(Window owner, string title, string message)
    {
        var dialog = CreateDialog(title, message, includeCancel: false, out _, out var okButton);
        okButton.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }

    public static async Task<bool> ShowConfirmAsync(Window owner, string title, string message)
    {
        var dialog = CreateDialog(title, message, includeCancel: true, out var cancelButton, out var okButton);
        bool result = false;

        okButton.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        cancelButton!.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(owner);
        return result;
    }

    private static Window CreateDialog(string title, string message, bool includeCancel, out Button? cancelButton, out Button okButton)
    {
        var accentColor = Color.Parse("#3ED6B4");
        var secondaryColor = Color.Parse("#3E56D6");

        cancelButton = null;
        okButton = new Button
        {
            Content = includeCancel ? "Continue" : "OK",
            MinWidth = 110,
            Background = new SolidColorBrush(accentColor),
            Foreground = Brushes.Black,
            Padding = new Thickness(16, 8),
            CornerRadius = new CornerRadius(8),
            FontWeight = FontWeight.Bold
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12
        };

        if (includeCancel)
        {
            cancelButton = new Button
            {
                Content = "Cancel",
                MinWidth = 110,
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Colors.White, 0.2),
                BorderThickness = new Thickness(1),
                Foreground = Brushes.White,
                Padding = new Thickness(16, 8),
                CornerRadius = new CornerRadius(8)
            };
            buttons.Children.Add(cancelButton);
        }

        buttons.Children.Add(okButton);

        return new Window
        {
            Title = title,
            Width = 480,
            Height = 260,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.Full,
            ExtendClientAreaToDecorationsHint = true,
            ExtendClientAreaTitleBarHeightHint = -1,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#0F111A"), 0),
                    new GradientStop(Color.Parse("#090C12"), 1)
                }
            },
            Content = new Border
            {
                Padding = new Thickness(32),
                Child = new StackPanel
                {
                    Spacing = 20,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title.ToUpper(),
                            FontSize = 18,
                            FontWeight = FontWeight.Black,
                            LetterSpacing = 1,
                            Foreground = new SolidColorBrush(accentColor)
                        },
                        new TextBlock
                        {
                            Text = message,
                            FontSize = 14,
                            LineHeight = 22,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.Parse("#CDD5E4"))
                        },
                        new Separator { Background = new SolidColorBrush(Colors.White, 0.05), Margin = new Thickness(0, 8) },
                        buttons
                    }
                }
            }
        };
    }

    public static async Task<string?> ShowTextInputAsync(Window owner, string title, string message, bool isPassword = false)
    {
        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 96,
            Background = new SolidColorBrush(Color.Parse("#3ED6B4")),
            Foreground = Brushes.Black
        };
        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 96
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12,
            Children = { cancelButton, okButton }
        };

        var input = new TextBox 
        { 
            Width = 400, 
            HorizontalAlignment = HorizontalAlignment.Left, 
            CornerRadius = new CornerRadius(12),
            PasswordChar = isPassword ? '*' : '\0'
        };

        var dialog = new Window
        {
            Title = title,
            Width = 500,
            Height = 300,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.Full,
            ExtendClientAreaToDecorationsHint = true,
            ExtendClientAreaTitleBarHeightHint = -1,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#0F111A"), 0),
                    new GradientStop(Color.Parse("#090C12"), 1)
                }
            },
            Content = new Border
            {
                Padding = new Thickness(32),
                Child = new StackPanel
                {
                    Spacing = 20,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title.ToUpper(),
                            FontSize = 18,
                            FontWeight = FontWeight.Black,
                            LetterSpacing = 1,
                            Foreground = new SolidColorBrush(Color.Parse("#3ED6B4"))
                        },
                        new TextBlock
                        {
                            Text = message,
                            FontSize = 14,
                            LineHeight = 22,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.Parse("#CDD5E4"))
                        },
                        input,
                        new Separator { Background = new SolidColorBrush(Colors.White, 0.05), Margin = new Thickness(0, 8) },
                        buttons
                    }
                }
            }
        };

        string? result = null;

        okButton.Click += (_, _) =>
        {
            result = input.Text;
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(owner);
        return result;
    }

    public static Window ShowModelessInfo(Window owner, string title, string message)
    {
        var okButton = new Button
        {
            Content = "Close",
            MinWidth = 96,
            Background = new SolidColorBrush(Color.Parse("#3ED6B4")),
            Foreground = Brushes.Black
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12,
            Children = { okButton }
        };

        var dialog = new Window
        {
            Title = title,
            Width = 460,
            Height = 220,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#121623")),
            Content = new Border
            {
                Padding = new Thickness(20),
                Child = new StackPanel
                {
                    Spacing = 18,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title,
                            FontSize = 20,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = Brushes.White
                        },
                        new TextBox
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            IsReadOnly = true,
                            Background = Brushes.Transparent,
                            BorderThickness = new Thickness(0),
                            Foreground = new SolidColorBrush(Color.Parse("#CDD5E4"))
                        },
                        buttons
                    }
                }
            }
        };

        okButton.Click += (_, _) => dialog.Close();

        dialog.Show(owner);
        return dialog;
    }

    public static async Task<bool> ShowMicrosoftAuthDialogAsync(Window owner, string userCode, string verificationUri, CancellationTokenSource cts)
    {
        var bg          = Color.Parse("#0D0F14");
        var surface     = Color.FromArgb(20, 255, 255, 255);
        var border      = Color.FromArgb(28, 255, 255, 255);
        var textPrimary = Color.Parse("#E8EDF5");
        var textMuted   = Color.Parse("#6C7A9C");
        var accent      = Color.Parse("#3B82F6"); // calm blue, not neon

        // ── Code block ─────────────────────────────────────────────────────
        var codeBlock = new Border
        {
            Background       = new SolidColorBrush(surface),
            BorderBrush      = new SolidColorBrush(border),
            BorderThickness  = new Thickness(1),
            CornerRadius     = new CornerRadius(10),
            Padding          = new Thickness(28, 14),
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor           = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text            = userCode,
                FontSize        = 32,
                FontWeight      = FontWeight.Bold,
                Foreground      = new SolidColorBrush(textPrimary),
                LetterSpacing   = 6,
                HorizontalAlignment = HorizontalAlignment.Center
            },
            Transitions = new Transitions
            {
                new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(150) }
            }
        };

        var copyHint = new TextBlock
        {
            Text       = "Click code to copy",
            FontSize   = 10,
            Foreground = new SolidColorBrush(textMuted),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin     = new Thickness(0, 4, 0, 0)
        };

        // ── Progress bar ───────────────────────────────────────────────────
        var progressBar = new ProgressBar
        {
            IsIndeterminate     = true,
            Height              = 2,
            Foreground          = new SolidColorBrush(accent),
            Background          = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
            CornerRadius        = new CornerRadius(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin              = new Thickness(0, 0, 0, 6)
        };

        // ── Buttons ────────────────────────────────────────────────────────
        var openBrowserBtn = new Button
        {
            Content    = "Open Browser",
            Padding    = new Thickness(20, 9),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(accent),
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold,
            FontSize   = 13,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };

        var cancelBtn = new Button
        {
            Content    = "Cancel",
            Padding    = new Thickness(20, 9),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            Foreground = new SolidColorBrush(textMuted),
            FontWeight = FontWeight.Medium,
            FontSize   = 13,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Transitions = new Transitions
            {
                new BrushTransition { Property = Button.ForegroundProperty, Duration = TimeSpan.FromMilliseconds(150) }
            }
        };
        cancelBtn.PointerEntered += (_, _) => cancelBtn.Foreground = new SolidColorBrush(textPrimary);
        cancelBtn.PointerExited  += (_, _) => cancelBtn.Foreground = new SolidColorBrush(textMuted);

        // ── Dialog window ─────────────────────────────────────────────────
        var dialog = new Window
        {
            Title                          = "Sign in with Microsoft",
            Width                          = 400,
            Height                         = 380,
            CanResize                      = false,
            WindowStartupLocation          = WindowStartupLocation.CenterOwner,
            SystemDecorations              = SystemDecorations.Full,
            ExtendClientAreaToDecorationsHint = true,
            ExtendClientAreaTitleBarHeightHint = -1,
            Background = new SolidColorBrush(bg),
            Content = new Border
            {
                Padding = new Thickness(32, 28, 32, 28),
                Child   = new StackPanel
                {
                    Spacing = 20,
                    Children =
                    {
                        // Header
                        new StackPanel
                        {
                            Spacing = 6,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text       = "Sign in with Microsoft",
                                    FontSize   = 18,
                                    FontWeight = FontWeight.Bold,
                                    Foreground = new SolidColorBrush(textPrimary),
                                    HorizontalAlignment = HorizontalAlignment.Center
                                },
                                new TextBlock
                                {
                                    Text       = $"Go to {verificationUri.Replace("https://", "")} and enter:",
                                    FontSize   = 12,
                                    Foreground = new SolidColorBrush(textMuted),
                                    HorizontalAlignment = HorizontalAlignment.Center
                                }
                            }
                        },

                        // Code + click-to-copy hint
                        new StackPanel
                        {
                            Spacing  = 0,
                            Children = { codeBlock, copyHint }
                        },

                        // Waiting indicator
                        new StackPanel
                        {
                            Spacing = 6,
                            Children =
                            {
                                progressBar,
                                new TextBlock
                                {
                                    Text       = "Waiting for you to sign in…",
                                    FontSize   = 11,
                                    Foreground = new SolidColorBrush(textMuted),
                                    HorizontalAlignment = HorizontalAlignment.Center
                                }
                            }
                        },

                        // Action buttons
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("*,12,*"),
                            Children =
                            {
                                openBrowserBtn.With(column: 0),
                                cancelBtn.With(column: 2)
                            }
                        }
                    }
                }
            }
        };

        // ── Interactions ──────────────────────────────────────────────────
        bool cancelled = false;

        // Click the code block to copy it
        codeBlock.PointerEntered += (_, _) => codeBlock.Background = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
        codeBlock.PointerExited  += (_, _) => codeBlock.Background = new SolidColorBrush(surface);
        codeBlock.PointerPressed += async (_, _) =>
        {
            var topLevel = TopLevel.GetTopLevel(dialog);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(userCode);
                copyHint.Text = "✓ Copied to clipboard";
                copyHint.Foreground = new SolidColorBrush(accent);
                await Task.Delay(1800);
                copyHint.Text = "Click code to copy";
                copyHint.Foreground = new SolidColorBrush(textMuted);
            }
        };

        openBrowserBtn.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo { FileName = verificationUri, UseShellExecute = true }); } catch { }
        };

        cancelBtn.Click += (_, _) =>
        {
            cancelled = true;
            try { cts.Cancel(); } catch { }
            dialog.Close();
        };

        dialog.Closed += (_, _) =>
        {
            if (!cancelled)
            {
                try { cts.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        };

        await dialog.ShowDialog(owner);
        return !cancelled;
    }
}
