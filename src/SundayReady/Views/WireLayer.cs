using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SundayReady.Models;
using SundayReady.ViewModels;

namespace SundayReady.Views;

/// <summary>
/// Draws every wire on the map, immediate-mode, from one clock.
/// <para>
/// One control rather than a Path per stroke, on purpose. The handoff's budget is ~35 connections
/// × 2 strokes sitting on a wall display for hours; seventy templated Paths each animating
/// StrokeDashOffset through the binding system is exactly the kind of churn that turns a booth PC
/// into a space heater. Here a single render pass draws every stroke with pens built on the spot,
/// and the only thing that "animates" is a timestamp.
/// </para>
/// <para>
/// The rendering rules are the handoff's, verbatim:
/// every wired connection is two stacked strokes on the same geometry — the cable (full colour,
/// low opacity, static) and the signal (dash <c>2 18</c>, drifting). Wireless has no cable at
/// all. Fail is the one pattern that is not flow: it pulses in place. Standby and starved do not
/// move — stillness is the signal.
/// </para>
/// </summary>
public sealed class WireLayer : Control
{
    public static readonly StyledProperty<System.Collections.Generic.IReadOnlyList<MapConnectionViewModel>?> ConnectionsProperty =
        AvaloniaProperty.Register<WireLayer, System.Collections.Generic.IReadOnlyList<MapConnectionViewModel>?>(nameof(Connections));

    public static readonly StyledProperty<bool> FrozenProperty =
        AvaloniaProperty.Register<WireLayer, bool>(nameof(Frozen));

    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private System.IDisposable? _frameTimer;

    public System.Collections.Generic.IReadOnlyList<MapConnectionViewModel>? Connections
    {
        get => GetValue(ConnectionsProperty);
        set => SetValue(ConnectionsProperty, value);
    }

    /// <summary>The "freeze wires" setting: the map must stay fully readable with no motion.</summary>
    public bool Frozen
    {
        get => GetValue(FrozenProperty);
        set => SetValue(FrozenProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // ~24fps. Dash drift at these speeds is imperceptibly smoother at 60, and the whole
        // point of this control is being cheap enough to leave running all Sunday.
        _frameTimer = Avalonia.Threading.DispatcherTimer.Run(
            () =>
            {
                if (!Frozen)
                {
                    InvalidateVisual();
                }

                return true;
            },
            TimeSpan.FromMilliseconds(42));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _frameTimer?.Dispose();
        _frameTimer = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>The wire under a point, for click-selection. Generous 6px slop either side.</summary>
    public MapConnectionViewModel? HitTest(Point point)
    {
        if (Connections is not { } connections)
        {
            return null;
        }

        var pen = new Pen(Brushes.Black, 12);
        foreach (var connection in connections)
        {
            if (connection.Geometry.StrokeContains(pen, point))
            {
                return connection;
            }
        }

        return null;
    }

    public override void Render(DrawingContext context)
    {
        if (Connections is not { } connections)
        {
            return;
        }

        var now = _clock.Elapsed.TotalSeconds;

        foreach (var connection in connections)
        {
            Draw(context, connection, now);
        }
    }

    private void Draw(DrawingContext context, MapConnectionViewModel wire, double now)
    {
        var geometry = wire.Geometry;
        var type = wire.Type;
        var colour = Color.TryParse(type.Colour, out var parsed) ? parsed : Colors.Gray;
        var state = wire.FlowState;

        // The legend's isolate filter dims rather than hides — an isolated map with missing
        // wires would look like missing cabling.
        var dim = wire.IsDimmed ? 0.22 : 1.0;
        var selected = wire.IsSelected;

        switch (state)
        {
            case "down":
            {
                // The reserved alarm: red dashes pulsing in place, opacity .3 → .95 → .3 over
                // 1.7s. It reads as an alarm precisely because it is the one thing not flowing.
                var pulse = Frozen ? 0.95 : 0.3 + (0.65 * Half(now, 1.7));
                var fail = Color.FromArgb((byte)(255 * 0.42 * dim), 0xff, 0x6b, 0x52);
                var failPulse = Color.FromArgb((byte)(255 * pulse * dim), 0xff, 0x6b, 0x52);

                context.DrawGeometry(null, new Pen(new SolidColorBrush(fail), 2.5), geometry);
                context.DrawGeometry(null, new Pen(new SolidColorBrush(failPulse), selected ? 3.5 : 3)
                {
                    DashStyle = new DashStyle(new double[] { 7 / 3.0, 7 / 3.0 }, 0),
                    LineCap = PenLineCap.Round,
                }, geometry);
                return;
            }

            case "standby":
            {
                // Grey, dashed, and deliberately not animated. Stillness is the signal.
                var grey = Color.FromArgb((byte)(255 * 0.60 * dim), 0x9b, 0xa3, 0xad);
                context.DrawGeometry(null, new Pen(new SolidColorBrush(grey), 2)
                {
                    DashStyle = new DashStyle(new double[] { 4.5, 4.5 }, 0),
                }, geometry);
                return;
            }

            case "starved":
            {
                // Nothing arriving. Faint and still — not red, because a starved hop is not a
                // broken hop, and painting it red would hide where the real break is.
                var faint = Color.FromArgb((byte)(255 * 0.26 * dim), 0xff, 0xff, 0xff);
                context.DrawGeometry(null, new Pen(new SolidColorBrush(faint), 2)
                {
                    DashStyle = new DashStyle(new double[] { 2, 4 }, 0),
                }, geometry);
                return;
            }
        }

        // live —
        var phase = Frozen ? 0 : now / wire.FlowSeconds;

        if (type.Wireless)
        {
            // No cable stroke at all: radio is present but not a physical object. Slower and
            // fainter than wired, drifting further per cycle.
            var wl = Color.FromArgb((byte)(255 * 0.80 * dim), colour.R, colour.G, colour.B);
            var offset = -(phase * 128) / type.StrokeWidth;

            if (wire.IsBidirectional)
            {
                DrawDuplexTrains(context, geometry, wl, type.StrokeWidth, 8, offset);
                return;
            }

            context.DrawGeometry(null, new Pen(new SolidColorBrush(wl), selected ? type.StrokeWidth + 1 : type.StrokeWidth)
            {
                DashStyle = new DashStyle(new double[] { 8 / type.StrokeWidth, 8 / type.StrokeWidth }, offset),
                LineCap = PenLineCap.Round,
            }, geometry);
            return;
        }

        // 1. The cable — the static physical run. A duplex run's cable is wider and dimmer:
        //    one fatter conduit carrying two conversations, per the handoff's 5a pattern.
        var cableOpacity = wire.IsBidirectional ? 0.30 : type.Id == "cat6" ? 0.42 : 0.36;
        var cableWidth = wire.IsBidirectional
            ? Math.Max(3.5, type.StrokeWidth + 1)
            : selected ? type.StrokeWidth + 1 : type.StrokeWidth;
        var cable = Color.FromArgb((byte)(255 * cableOpacity * dim), colour.R, colour.G, colour.B);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(cable), cableWidth), geometry);

        var signal = Color.FromArgb((byte)(255 * 1.00 * dim), colour.R, colour.G, colour.B);

        if (wire.IsBidirectional)
        {
            DrawDuplexTrains(context, geometry, signal, 2, 2, -(phase * 64) / 2);
            return;
        }

        // 2. The signal — dash "2 18" drifting -64 per cycle. Avalonia's dash units are
        //    multiples of stroke width, so the handoff's pixel values divide by it.
        var signalOffset = -(phase * 64) / type.StrokeWidth;

        context.DrawGeometry(null, new Pen(new SolidColorBrush(signal), selected ? type.StrokeWidth + 1 : type.StrokeWidth)
        {
            DashStyle = new DashStyle(new double[] { 2 / type.StrokeWidth, 18 / type.StrokeWidth }, signalOffset),
            LineCap = PenLineCap.Round,
        }, geometry);
    }

    /// <summary>
    /// The duplex pattern: two thin dash trains 6px apart on the same run, one flowing forward
    /// and one flowing back.
    /// <para>
    /// The handoff is emphatic about what NOT to do here — never counter-moving dashes on a
    /// single line, which reads as a fault rather than as two directions. The perpendicular
    /// offset is approximated with a vertical translate, which holds because the map's béziers
    /// enter and leave horizontally; a wire would need to run nearly vertical before the two
    /// trains visibly converged, and this graph's columns make that rare.
    /// </para>
    /// </summary>
    private static void DrawDuplexTrains(
        DrawingContext context,
        Geometry geometry,
        Color colour,
        double strokeWidth,
        double dashOn,
        double offset)
    {
        var brush = new SolidColorBrush(colour);
        var gap = dashOn == 2 ? 18d : dashOn;

        using (context.PushTransform(Matrix.CreateTranslation(0, -3)))
        {
            context.DrawGeometry(null, new Pen(brush, strokeWidth)
            {
                DashStyle = new DashStyle(new[] { dashOn / strokeWidth, gap / strokeWidth }, offset),
                LineCap = PenLineCap.Round,
            }, geometry);
        }

        using (context.PushTransform(Matrix.CreateTranslation(0, 3)))
        {
            context.DrawGeometry(null, new Pen(brush, strokeWidth)
            {
                DashStyle = new DashStyle(new[] { dashOn / strokeWidth, gap / strokeWidth }, -offset),
                LineCap = PenLineCap.Round,
            }, geometry);
        }
    }

    /// <summary>A 0→1→0 triangle wave with the given period — the fail pulse.</summary>
    private static double Half(double now, double period)
    {
        var t = now % period / period;
        return t < 0.5 ? t * 2 : (1 - t) * 2;
    }
}

/// <summary>
/// One legend row's live sample: a short line drawn with exactly the same technique as the map,
/// so the legend is a key, not a static swatch.
/// </summary>
public sealed class WireSample : Control
{
    public static readonly StyledProperty<MapConnectionType?> TypeProperty =
        AvaloniaProperty.Register<WireSample, MapConnectionType?>(nameof(Type));

    public static readonly StyledProperty<bool> IsFailSampleProperty =
        AvaloniaProperty.Register<WireSample, bool>(nameof(IsFailSample));

    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private System.IDisposable? _frameTimer;

    public MapConnectionType? Type
    {
        get => GetValue(TypeProperty);
        set => SetValue(TypeProperty, value);
    }

    public bool IsFailSample
    {
        get => GetValue(IsFailSampleProperty);
        set => SetValue(IsFailSampleProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _frameTimer = Avalonia.Threading.DispatcherTimer.Run(
            () =>
            {
                InvalidateVisual();
                return true;
            },
            TimeSpan.FromMilliseconds(60));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _frameTimer?.Dispose();
        _frameTimer = null;
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        var y = Bounds.Height / 2;
        var line = new LineGeometry(new Point(1, y), new Point(Bounds.Width - 1, y));
        var now = _clock.Elapsed.TotalSeconds;

        if (IsFailSample)
        {
            var pulse = 0.3 + (0.65 * (now % 1.7 / 1.7 < 0.5 ? now % 1.7 / 1.7 * 2 : (1 - (now % 1.7 / 1.7)) * 2));
            context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(255 * pulse), 0xff, 0x6b, 0x52)), 3)
            {
                DashStyle = new DashStyle(new double[] { 7 / 3.0, 7 / 3.0 }, 0),
                LineCap = PenLineCap.Round,
            }, line);
            return;
        }

        if (Type is not { } type || !Color.TryParse(type.Colour, out var colour))
        {
            return;
        }

        if (type.Wireless)
        {
            context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(255 * 0.80), colour.R, colour.G, colour.B)), type.StrokeWidth)
            {
                DashStyle = new DashStyle(new double[] { 8 / type.StrokeWidth, 8 / type.StrokeWidth }, -(now / 6.6 * 128) / type.StrokeWidth),
                LineCap = PenLineCap.Round,
            }, line);
            return;
        }

        context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(255 * 0.36), colour.R, colour.G, colour.B)), type.StrokeWidth), line);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(255 * 1.00), colour.R, colour.G, colour.B)), type.StrokeWidth)
        {
            DashStyle = new DashStyle(new double[] { 2 / type.StrokeWidth, 18 / type.StrokeWidth }, -(now / type.FlowSeconds * 64) / type.StrokeWidth),
            LineCap = PenLineCap.Round,
        }, line);
    }
}
