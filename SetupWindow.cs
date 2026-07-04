using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;

namespace OfflineMinecraftLauncher;

public class SetupWindow : Window
{
    public SetupWindow()
    {
        Title = "Fugo Launcher - Setup";
        Width = 450;
        Height = 220;
        Background = new SolidColorBrush(Color.Parse("#0D1117"));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SystemDecorations = SystemDecorations.Full;
        Topmost = true;

        Content = new Border
        {
            Padding = new Thickness(24),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 110, 91, 255)),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Wait till we setup the launcher...",
                        FontSize = 18,
                        FontWeight = FontWeight.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "Installing necessary components and creating shortcuts.",
                        FontSize = 13,
                        Foreground = Brushes.Gray,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new Border
                    {
                        Padding = new Thickness(12, 8),
                        Background = new SolidColorBrush(Color.FromArgb(30, 255, 87, 87)),
                        BorderBrush = new SolidColorBrush(Color.Parse("#FF5757")),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Child = new TextBlock
                        {
                            Text = "DISCLAIMER: Do not delete the exe file as it is the main launcher.",
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Color.Parse("#FF9B9B")),
                            FontStyle = FontStyle.Italic,
                            TextWrapping = TextWrapping.Wrap,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextAlignment = TextAlignment.Center
                        }
                    }
                }
            }
        };
    }
}
