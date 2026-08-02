using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OfflineMinecraftLauncher;

/// <summary>
/// Custom-drawn startup card: white background with curvy lines.
/// SlideProgress (0→1) slides a black panel from left to right,
/// changing the pattern colour to faint white.
/// </summary>
public class StartupPatternCard : Control
{
    public static readonly StyledProperty<double> SlideProgressProperty =
        AvaloniaProperty.Register<StartupPatternCard, double>(nameof(SlideProgress), 0.0);

    public double SlideProgress
    {
        get => GetValue(SlideProgressProperty);
        set => SetValue(SlideProgressProperty, value);
    }

    static StartupPatternCard()
    {
        AffectsRender<StartupPatternCard>(SlideProgressProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var fullRect = new Rect(0, 0, w, h);

        using (context.PushClip(fullRect))
        {
            // ── White layer ──────────────────────────────────────────────────
            context.FillRectangle(Brushes.White, fullRect);

            // Subtle dark pattern on white
            var darkPen = new Pen(new SolidColorBrush(Color.FromArgb(55, 150, 165, 185)), 1.5);
            DrawCurvyLines(context, w, h, darkPen);

            // ── Black slide layer (clips from left) ──────────────────────────
            double slideW = w * Math.Clamp(SlideProgress, 0.0, 1.0);
            if (slideW > 0)
            {
                var slideClip = new Rect(0, 0, slideW, h);
                using (context.PushClip(slideClip))
                {
                    context.FillRectangle(new SolidColorBrush(Color.Parse("#0B0B0E")), fullRect);

                    // Faint white pattern on black
                    var whitePen = new Pen(new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)), 1.5);
                    DrawCurvyLines(context, w, h, whitePen);
                }
            }
        }
    }

    private static void DrawCurvyLines(DrawingContext ctx, double w, double h, Pen pen)
    {
        var geo = new StreamGeometry();
        using (var c = geo.Open())
        {
            c.BeginFigure(new Point(-0.1 * w, 0.25 * h), false);
            c.CubicBezierTo(new Point(0.3 * w, 0.05 * h), new Point(0.65 * w, 0.5 * h),  new Point(1.1 * w, 0.2 * h));

            c.BeginFigure(new Point(-0.05 * w, 0.6 * h), false);
            c.CubicBezierTo(new Point(0.25 * w, 0.2 * h), new Point(0.7 * w, 0.85 * h), new Point(1.05 * w, 0.45 * h));

            c.BeginFigure(new Point(-0.1 * w, 0.85 * h), false);
            c.CubicBezierTo(new Point(0.35 * w, 0.55 * h), new Point(0.6 * w, 0.95 * h), new Point(1.1 * w, 0.7 * h));

            c.BeginFigure(new Point(0.1 * w, -0.1 * h), false);
            c.CubicBezierTo(new Point(0.4 * w, 0.4 * h), new Point(0.8 * w, -0.05 * h), new Point(1.05 * w, 0.15 * h));

            c.BeginFigure(new Point(-0.05 * w, 0.4 * h), false);
            c.CubicBezierTo(new Point(0.2 * w, 0.9 * h), new Point(0.75 * w, 0.3 * h),  new Point(1.1 * w, 0.9 * h));

            c.BeginFigure(new Point(0.2 * w, 1.1 * h), false);
            c.CubicBezierTo(new Point(0.5 * w, 0.3 * h), new Point(0.75 * w, 0.7 * h),  new Point(0.95 * w, -0.1 * h));
        }
        ctx.DrawGeometry(null, pen, geo);
    }
}

// Stub — no longer used as a standalone window.
public class SplashWindow : Avalonia.Controls.Window
{
    public SplashWindow() { }
}
