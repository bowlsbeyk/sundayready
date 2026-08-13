using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SundayReady.Controls;

/// <summary>
/// The rail's progress ring: a track, a completed arc, and — when something is failing — a
/// short arc after it for the failing share. Two SVG circles in the design; two arcs here.
/// <para>
/// The completed arc is green normally and amber whenever the tab has a failing verifier, so
/// the ring never reads as celebratory while something is broken.
/// </para>
/// </summary>
public sealed class ProgressRing : Control
{
    /// <summary>Gap between the completed arc and the failing arc, as a fraction of the circle.</summary>
    private const double ArcGap = 0.014;

    public static readonly StyledProperty<double> CompletedFractionProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(CompletedFraction));

    public static readonly StyledProperty<double> FailingFractionProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(FailingFraction));

    public static readonly StyledProperty<bool> IsHealthyProperty =
        AvaloniaProperty.Register<ProgressRing, bool>(nameof(IsHealthy), true);

    public static readonly StyledProperty<double> RingThicknessProperty =
        AvaloniaProperty.Register<ProgressRing, double>(nameof(RingThickness), 14d);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> OkBrushProperty =
        AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(OkBrush));

    public static readonly StyledProperty<IBrush?> WaitBrushProperty =
        AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(WaitBrush));

    public static readonly StyledProperty<IBrush?> FailBrushProperty =
        AvaloniaProperty.Register<ProgressRing, IBrush?>(nameof(FailBrush));

    static ProgressRing()
    {
        AffectsRender<ProgressRing>(
            CompletedFractionProperty, FailingFractionProperty, IsHealthyProperty,
            RingThicknessProperty, TrackBrushProperty, OkBrushProperty, WaitBrushProperty, FailBrushProperty);
    }

    public double CompletedFraction
    {
        get => GetValue(CompletedFractionProperty);
        set => SetValue(CompletedFractionProperty, value);
    }

    public double FailingFraction
    {
        get => GetValue(FailingFractionProperty);
        set => SetValue(FailingFractionProperty, value);
    }

    public bool IsHealthy
    {
        get => GetValue(IsHealthyProperty);
        set => SetValue(IsHealthyProperty, value);
    }

    public double RingThickness
    {
        get => GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? OkBrush
    {
        get => GetValue(OkBrushProperty);
        set => SetValue(OkBrushProperty, value);
    }

    public IBrush? WaitBrush
    {
        get => GetValue(WaitBrushProperty);
        set => SetValue(WaitBrushProperty, value);
    }

    public IBrush? FailBrush
    {
        get => GetValue(FailBrushProperty);
        set => SetValue(FailBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var extent = Math.Min(Bounds.Width, Bounds.Height);
        if (extent <= RingThickness)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = (extent - RingThickness) / 2;

        if (TrackBrush is not null)
        {
            context.DrawEllipse(null, new Pen(TrackBrush, RingThickness), center, radius, radius);
        }

        var completed = Math.Clamp(CompletedFraction, 0, 1);
        var failing = Math.Clamp(FailingFraction, 0, 1 - completed);

        var completedBrush = IsHealthy ? OkBrush : WaitBrush;
        if (completedBrush is not null)
        {
            DrawArc(context, center, radius, 0, completed, new Pen(completedBrush, RingThickness, lineCap: PenLineCap.Round));
        }

        if (failing > 0 && FailBrush is not null)
        {
            DrawArc(context, center, radius, completed + ArcGap, failing,
                new Pen(FailBrush, RingThickness, lineCap: PenLineCap.Round));
        }
    }

    private static void DrawArc(DrawingContext context, Point center, double radius, double from, double sweep, IPen pen)
    {
        if (sweep <= 0)
        {
            return;
        }

        // A full circle has no start/end to arc between, so draw it as an ellipse instead.
        if (sweep >= 0.999)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        // Twelve o'clock is -90° in screen coordinates; the design's ring is rotated to match.
        var startAngle = -90 + (from * 360);
        var sweepAngle = sweep * 360;

        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(PointOnCircle(center, radius, startAngle), false);
            sink.ArcTo(
                PointOnCircle(center, radius, startAngle + sweepAngle),
                new Size(radius, radius),
                0,
                sweepAngle > 180,
                SweepDirection.Clockwise);
            sink.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(center.X + (radius * Math.Cos(radians)), center.Y + (radius * Math.Sin(radians)));
    }
}
