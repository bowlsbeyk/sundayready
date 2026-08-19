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
        DrawCore(context, wire, now);
        DrawPollBlip(context, wire);
    }

    /// <summary>
    /// The request/response pattern from the handoff's 5a: when a wire's own verifier runs, a
    /// short blue dash travels out and a green (or red) one comes back. An overlay, not a
    /// FlowState — it rides on top of live flow and even on top of the red alarm, because a
    /// failing wire that is still being retried should visibly still be checked.
    /// </summary>
    private void DrawPollBlip(DrawingContext context, MapConnectionViewModel wire)
    {
        if (Frozen || wire.LastPolledAt is not { } polled)
        {
            return;
        }

        var age = (DateTime.UtcNow - polled).TotalSeconds;
        var length = wire.PathLength;

        if (age is < 0 or >= 1.0 || length < 30)
        {
            return;
        }

        // Out on the request, back on the answer.
        double t;
        Color colour;

        if (age < 0.5)
        {
            t = age / 0.5;
            colour = Color.Parse("#5aa9ff");
        }
        else
        {
            t = 1 - ((age - 0.5) / 0.5);
            colour = wire.IsOk ? Color.Parse("#4ade9a") : Color.Parse("#ff6b52");
        }

        // Fade the first and last 60ms so the blip never pops in or out.
        var fade = Math.Clamp(Math.Min(age / 0.06, (1.0 - age) / 0.06), 0, 1);

        WireStrokes.Traveler(context, wire.Geometry, colour, 3, 14, length, t, 0.95 * fade);
    }

    private void DrawCore(DrawingContext context, MapConnectionViewModel wire, double now)
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
                WireStrokes.FailPulse(context, geometry, now, Frozen, dim, selected);
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

        // Heartbeat types carry occasional traffic: a faint cable, still, with one short pulse
        // travelling the run every few seconds. A continuous flow train on a link that talks
        // once a minute would be a lie about what the cable does.
        if (type.Pulse)
        {
            if (!type.Wireless)
            {
                WireStrokes.Cable(context, geometry, colour,
                    selected ? type.StrokeWidth + 1 : type.StrokeWidth, 0.30 * dim);
            }

            if (!Frozen)
            {
                var period = Math.Max(3.6, wire.FlowSeconds);
                var cycle = now % period / period;

                // The pulse occupies the first fifth of the cycle; the rest is stillness.
                if (cycle < 0.2)
                {
                    var tp = cycle / 0.2;
                    var beatFade = Math.Clamp(Math.Min(tp / 0.12, (1 - tp) / 0.12), 0, 1);
                    WireStrokes.Traveler(context, geometry, colour, type.StrokeWidth + 0.5, 10,
                        wire.PathLength, tp, 0.9 * beatFade * dim);
                }
            }

            return;
        }

        // An unknown type is "no information", and no information must not be confusable with
        // a wireless link: dotted and deliberately still, because the map cannot claim signal
        // is flowing over a run whose nature it does not know.
        if (type.Id == "unknown")
        {
            var grey = Color.FromArgb((byte)(255 * 0.55 * dim), colour.R, colour.G, colour.B);
            context.DrawGeometry(null, new Pen(new SolidColorBrush(grey), selected ? type.StrokeWidth + 1 : type.StrokeWidth)
            {
                DashStyle = new DashStyle(new double[] { 1 / type.StrokeWidth, 3 / type.StrokeWidth }, 0),
                LineCap = PenLineCap.Round,
            }, geometry);
            return;
        }

        // live —
        var phase = Frozen ? 0 : now / wire.FlowSeconds;

        if (type.Wireless)
        {
            // No cable stroke at all: radio is present but not a physical object. Slower and
            // fainter than wired, drifting further per cycle.
            var offset = -(phase * 128) / type.StrokeWidth;

            if (wire.IsBidirectional)
            {
                DrawDuplexTrains(context, wire, colour, 0.80 * dim, type.StrokeWidth, 8, offset);
                return;
            }

            WireStrokes.Train(
                context, geometry, colour,
                selected ? type.StrokeWidth + 1 : type.StrokeWidth,
                8, 8, offset, 0.80 * dim);
            return;
        }

        // 1. The cable — the static physical run. A duplex run's cable is wider and dimmer:
        //    one fatter conduit carrying two conversations, per the handoff's 5a pattern.
        var cableOpacity = wire.IsBidirectional ? 0.30 : type.Id == "cat6" ? 0.42 : 0.36;
        var cableWidth = wire.IsBidirectional
            ? Math.Max(3.5, type.StrokeWidth + 1)
            : selected ? type.StrokeWidth + 1 : type.StrokeWidth;
        WireStrokes.Cable(context, geometry, colour, cableWidth, cableOpacity * dim);

        if (wire.IsBidirectional)
        {
            DrawDuplexTrains(context, wire, colour, 1.00 * dim, 2, 2, -(phase * 64) / 2);
            return;
        }

        // 2. The signal — dash "2 18" drifting -64 per cycle.
        WireStrokes.Train(
            context, geometry, colour,
            selected ? type.StrokeWidth + 1 : type.StrokeWidth,
            2, 18, -(phase * 64) / type.StrokeWidth, 1.00 * dim);
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
        MapConnectionViewModel wire,
        Color colour,
        double opacity,
        double strokeWidth,
        double dashOn,
        double offset)
    {
        var gap = dashOn == 2 ? 18d : dashOn;

        // Each train rides a copy of the path shifted along its own NORMAL. The previous
        // vertical context-translate held only where the curve ran horizontal; through the
        // middle of a tall S-bend it slid the trains along the path instead of across it and
        // they merged into what read as a fault. The offset geometries are cached on the wire
        // and rebuilt only on layout change, so nothing here allocates geometry per frame.
        WireStrokes.Train(context, wire.OffsetGeometry(-3), colour, strokeWidth, dashOn, gap, offset, opacity);
        WireStrokes.Train(context, wire.OffsetGeometry(3), colour, strokeWidth, dashOn, gap, -offset, opacity);
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

    public static readonly StyledProperty<bool> FrozenProperty =
        AvaloniaProperty.Register<WireSample, bool>(nameof(Frozen));

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

    /// <summary>
    /// Follows the map's freeze setting. A frozen map with an animating legend defeats the
    /// setting on exactly the slow booth PCs it exists for.
    /// </summary>
    public bool Frozen
    {
        get => GetValue(FrozenProperty);
        set => SetValue(FrozenProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _frameTimer = Avalonia.Threading.DispatcherTimer.Run(
            () =>
            {
                if (!Frozen)
                {
                    InvalidateVisual();
                }

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
        var now = Frozen ? 0 : _clock.Elapsed.TotalSeconds;

        // The same strokes the map draws, via the same helpers - the legend is a key, not an
        // approximation of one.
        if (IsFailSample)
        {
            WireStrokes.FailPulse(context, line, now, Frozen);
            return;
        }

        if (Type is not { } type || !Color.TryParse(type.Colour, out var colour))
        {
            return;
        }

        if (type.Pulse)
        {
            WireStrokes.Cable(context, line, colour, type.StrokeWidth, 0.30);

            var cycle = now % 4.2 / 4.2;

            if (!Frozen && cycle < 0.2)
            {
                var tp = cycle / 0.2;
                var beatFade = Math.Clamp(Math.Min(tp / 0.12, (1 - tp) / 0.12), 0, 1);
                WireStrokes.Traveler(context, line, colour, type.StrokeWidth + 0.5, 10,
                    Math.Max(20, Bounds.Width - 2), tp, 0.9 * beatFade);
            }

            return;
        }

        if (type.Wireless)
        {
            WireStrokes.Train(context, line, colour, type.StrokeWidth,
                8, 8, -(now / 6.6 * 128) / type.StrokeWidth, 0.80);
            return;
        }

        WireStrokes.Cable(context, line, colour, type.StrokeWidth, 0.36);
        WireStrokes.Train(context, line, colour, type.StrokeWidth,
            2, 18, -(now / type.FlowSeconds * 64) / type.StrokeWidth, 1.00);
    }
}
