using Avalonia.Media;

namespace SundayReady.Views;

/// <summary>
/// The three strokes every wire drawing is made of, shared by <see cref="WireLayer"/> and
/// <see cref="WireSample"/>. One home for the pen maths, because the legend is a key to the map
/// and two copies of the rendering rules would drift until it lied.
/// </summary>
internal static class WireStrokes
{
    /// <summary>The static cable — the physical run, full colour at low opacity.</summary>
    public static void Cable(DrawingContext context, Geometry geometry, Color colour, double width, double opacity)
    {
        var paint = Color.FromArgb((byte)(255 * opacity), colour.R, colour.G, colour.B);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(paint), width), geometry);
    }

    /// <summary>
    /// A drifting dash train. Dash units are multiples of stroke width, so pixel values divide
    /// by it; the offset is expected pre-divided the same way.
    /// </summary>
    public static void Train(
        DrawingContext context,
        Geometry geometry,
        Color colour,
        double width,
        double dashOn,
        double gap,
        double offset,
        double opacity)
    {
        var paint = Color.FromArgb((byte)(255 * opacity), colour.R, colour.G, colour.B);

        context.DrawGeometry(null, new Pen(new SolidColorBrush(paint), width)
        {
            DashStyle = new DashStyle(new[] { dashOn / width, gap / width }, offset),
            LineCap = PenLineCap.Round,
        }, geometry);
    }

    /// <summary>
    /// The reserved alarm: red dashes pulsing in place, opacity .3 → .95 → .3 over 1.7s. It
    /// reads as an alarm precisely because it is the one thing not flowing. Frozen pins it at
    /// full brightness — a frozen map must still show its faults.
    /// </summary>
    public static void FailPulse(
        DrawingContext context,
        Geometry geometry,
        double now,
        bool frozen,
        double dim = 1.0,
        bool selected = false)
    {
        var cycle = now % 1.7 / 1.7;
        var pulse = frozen ? 0.95 : 0.3 + (0.65 * (cycle < 0.5 ? cycle * 2 : (1 - cycle) * 2));
        var fail = Color.FromArgb((byte)(255 * 0.42 * dim), 0xff, 0x6b, 0x52);
        var failPulse = Color.FromArgb((byte)(255 * pulse * dim), 0xff, 0x6b, 0x52);
        var width = selected ? 3.5 : 3.0;

        context.DrawGeometry(null, new Pen(new SolidColorBrush(fail), 2.5), geometry);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(failPulse), width)
        {
            DashStyle = new DashStyle(new[] { 7 / width, 7 / width }, 0),
            LineCap = PenLineCap.Round,
        }, geometry);
    }
}
