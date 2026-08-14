using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SundayReady.Controls;

/// <summary>
/// The telemetry rail's bar sparkline. Draws the most recent <see cref="MaxBars"/> samples,
/// scaled to the largest of them, with the last <see cref="RecentBars"/> in the accent colour.
/// <para>
/// Draws nothing when there are no samples — the rail renders its own empty state instead.
/// This control never invents a shape to fill the space.
/// </para>
/// </summary>
public sealed class Sparkline : Control
{
    private const double Gap = 3;
    private const double Radius = 2;

    /// <summary>Below this fraction a bar is still drawn as a stub, so a zero sample is visible.</summary>
    private const double MinimumFraction = 0.06;

    public static readonly StyledProperty<IEnumerable?> SamplesProperty =
        AvaloniaProperty.Register<Sparkline, IEnumerable?>(nameof(Samples));

    public static readonly StyledProperty<int> MaxBarsProperty =
        AvaloniaProperty.Register<Sparkline, int>(nameof(MaxBars), 12);

    public static readonly StyledProperty<int> RecentBarsProperty =
        AvaloniaProperty.Register<Sparkline, int>(nameof(RecentBars), 5);

    public static readonly StyledProperty<IBrush?> BarBrushProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(BarBrush));

    public static readonly StyledProperty<IBrush?> RecentBrushProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(RecentBrush));

    static Sparkline()
    {
        AffectsRender<Sparkline>(
            SamplesProperty, MaxBarsProperty, RecentBarsProperty, BarBrushProperty, RecentBrushProperty);
    }

    public IEnumerable? Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public int MaxBars
    {
        get => GetValue(MaxBarsProperty);
        set => SetValue(MaxBarsProperty, value);
    }

    public int RecentBars
    {
        get => GetValue(RecentBarsProperty);
        set => SetValue(RecentBarsProperty, value);
    }

    public IBrush? BarBrush
    {
        get => GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public IBrush? RecentBrush
    {
        get => GetValue(RecentBrushProperty);
        set => SetValue(RecentBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var values = Read();
        if (values.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var peak = values.Max();
        var width = (Bounds.Width - Gap * (values.Count - 1)) / values.Count;
        if (width <= 0)
        {
            return;
        }

        for (var index = 0; index < values.Count; index++)
        {
            var fraction = peak <= 0 ? MinimumFraction : Math.Max(values[index] / peak, MinimumFraction);
            var height = Bounds.Height * fraction;
            var brush = index >= values.Count - RecentBars ? RecentBrush : BarBrush;

            if (brush is null)
            {
                continue;
            }

            var rect = new Rect(index * (width + Gap), Bounds.Height - height, width, height);
            context.DrawRectangle(brush, null, new RoundedRect(rect, Radius));
        }
    }

    private List<double> Read()
    {
        if (Samples is null)
        {
            return new List<double>();
        }

        var values = new List<double>();
        foreach (var sample in Samples)
        {
            if (sample is IConvertible convertible)
            {
                values.Add(convertible.ToDouble(null));
            }
        }

        return values.Count <= MaxBars ? values : values.GetRange(values.Count - MaxBars, MaxBars);
    }
}
