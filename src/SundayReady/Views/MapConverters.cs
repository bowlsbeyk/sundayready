using System.Globalization;
using Avalonia.Data.Converters;

namespace SundayReady.Views;

/// <summary>
/// Small one-way converters the map's templates need. Kept here rather than as view-model
/// properties because they are presentation arithmetic — a port tick's width is a drawing
/// decision, and a view model that knows about pixel widths has stopped being a view model.
/// </summary>
public static class MapConverters
{
    /// <summary>
    /// A port tick is 6px wide, or 11px when more than one run shares the socket. The extra width
    /// is the only cue that two cables land on one jack, so it has to survive a glance.
    /// </summary>
    public static readonly IValueConverter PortTickWidth =
        new FuncValueConverter<bool, double>(shared => shared ? 11 : 6);

    /// <summary>Isolate-a-signal-type dimming, matching the wire layer's own .22.</summary>
    public static readonly IValueConverter DimOpacity =
        new FuncValueConverter<bool, double>(dimmed => dimmed ? 0.22 : 1.0);

    /// <summary>Depth as a left margin — the stream fan's indent.</summary>
    public static readonly IValueConverter LeftIndent =
        new FuncValueConverter<double, Avalonia.Thickness>(d => new Avalonia.Thickness(d, 0, 0, 7));

    /// <summary>The zoom factor as a layout transform for the canvas wrapper.</summary>
    public static readonly IValueConverter ZoomTransform =
        new FuncValueConverter<double, Avalonia.Media.ITransform>(z =>
            new Avalonia.Media.ScaleTransform(z <= 0 ? 1 : z, z <= 0 ? 1 : z));
}
