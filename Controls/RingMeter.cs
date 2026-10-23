using System;
using System.Windows;
using System.Windows.Media;

namespace AutoShutdown.Controls;

/// <summary>
/// A flat pie countdown meter in the style of a minimal wall clock: white face,
/// a 60-dash tick border around the rim, and the remaining time as a solid
/// wedge swept clockwise from 12 o'clock. No glow, no ramp - just the pie
/// eating away to nothing as the countdown finishes.
/// </summary>
public sealed class RingMeter : FrameworkElement
{
    private static readonly Brush DefaultTrack = Freeze(new SolidColorBrush(Colors.White));
    private static readonly Brush DefaultWedge = Freeze(new SolidColorBrush(BaseWedgeColor));
    private static readonly Color BaseWedgeColor = Color.FromRgb(0x43, 0x76, 0x6C);

    // Clock-border dashes: pale for the five-minute dots, darker for the hours.
    private static readonly Brush MinorTickBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xA9, 0xAE, 0xBE)));
    private static readonly Brush MajorTickBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x54, 0x5C, 0x6E)));
    private static readonly Pen MinorTickPen = Freeze(MakePen(MinorTickBrush, 1.7));
    private static readonly Pen MajorTickPen = Freeze(MakePen(MajorTickBrush, 3.0));

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress),
        typeof(double),
        typeof(RingMeter),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender, null, CoerceProgress));

    public static readonly DependencyProperty WedgeBrushProperty = DependencyProperty.Register(
        nameof(WedgeBrush),
        typeof(Brush),
        typeof(RingMeter),
        new FrameworkPropertyMetadata(DefaultWedge, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(Brush),
        typeof(RingMeter),
        new FrameworkPropertyMetadata(DefaultTrack, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Remaining fraction of the countdown: 1 = full pie, 0 = empty.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public Brush? WedgeBrush
    {
        get => (Brush?)GetValue(WedgeBrushProperty);
        set => SetValue(WedgeBrushProperty, value);
    }

    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        var radius = size / 2 - 1;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);

        // White clock face with a whisper of depth toward the rim.
        dc.DrawEllipse(FaceFill(center, radius), null, center, radius, radius);

        var progress = Math.Clamp(Progress, 0, 1);

        if (progress >= 0.9999)
        {
            dc.DrawEllipse(WedgeFill(center, radius), null, center, radius, radius);
        }
        else if (progress > 0)
        {
            dc.DrawGeometry(WedgeFill(center, radius), null, BuildPie(center, radius, progress));
        }

        // Tick border: 60 dashes, heavier at every five-minute mark.
        var outerRadius = radius * 0.985;
        for (var tick = 0; tick < 60; tick++)
        {
            var angle = tick * 6 - 90; // 12 o'clock start
            var major = tick % 5 == 0;
            var innerRadius = outerRadius - size * (major ? 0.055 : 0.028);

            dc.DrawLine(
                major ? MajorTickPen : MinorTickPen,
                PointOnCircle(center, outerRadius, angle),
                PointOnCircle(center, innerRadius, angle));
        }
    }

    private static Pen MakePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        return pen;
    }

    /// <summary>
    /// Slight radial depth inside the clock face: pure white at the heart,
    /// cooling a touch toward the rim so the disc reads as a real dial.
    /// </summary>
    private static Brush FaceFill(Point center, double radius)
    {
        var stops = new GradientStopCollection
        {
            new GradientStop(Colors.White, 0.0),
            new GradientStop(Colors.White, 0.62),
            new GradientStop(Color.FromRgb(0xF0, 0xF1, 0xF7), 1.0)
        };
        return CreateRadial(stops, center, radius);
    }

    /// <summary>
    /// Slight radial depth inside the pie: a touch lighter at the heart,
    /// easing back through the flat brand colour, then a hair darker at the
    /// rim. Absolutely mapped so the gradient stays still while the wedge is
    /// eaten away.
    /// </summary>
    private Brush WedgeFill(Point center, double radius)
    {
        var baseColor = ((WedgeBrush ?? DefaultWedge) as SolidColorBrush)?.Color
                        ?? BaseWedgeColor;

        var stops = new GradientStopCollection
        {
            new GradientStop(Lift(baseColor, 0.14), 0.0),
            new GradientStop(baseColor, 0.62),
            new GradientStop(Sink(baseColor, 0.07), 1.0)
        };
        return CreateRadial(stops, center, radius);
    }

    private static Brush CreateRadial(GradientStopCollection stops, Point center, double radius)
    {
        var brush = new RadialGradientBrush(stops)
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = center,
            GradientOrigin = center,
            RadiusX = radius * 0.94,
            RadiusY = radius * 0.94
        };
        brush.Freeze();
        return brush;
    }

    private static Color Lift(Color c, double amount)
        => Color.FromRgb(
            (byte)(c.R + (255 - c.R) * amount),
            (byte)(c.G + (255 - c.G) * amount),
            (byte)(c.B + (255 - c.B) * amount));

    private static Color Sink(Color c, double amount)
        => Color.FromRgb(
            (byte)(c.R * (1 - amount)),
            (byte)(c.G * (1 - amount)),
            (byte)(c.B * (1 - amount)));

    private static StreamGeometry BuildPie(Point center, double radius, double progress)
    {
        const double startAngle = -90; // 12 o'clock
        var sweep = 360 * progress;
        var endAngle = startAngle + sweep;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(center, true, true);
            ctx.LineTo(PointOnCircle(center, radius, startAngle), true, false);
            ctx.ArcTo(
                PointOnCircle(center, radius, endAngle),
                new Size(radius, radius),
                0,
                sweep > 180,
                SweepDirection.Clockwise,
                true,
                false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private static object CoerceProgress(DependencyObject d, object baseValue)
        => Math.Clamp((double)baseValue, 0, 1);

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
