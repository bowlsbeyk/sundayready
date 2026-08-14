using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SundayReady.Controls;

/// <summary>
/// The 5px bar under a techdesk station header: a track, the completed share, and — when
/// something on that station is failing — a short failing segment after it.
/// <para>
/// A control rather than two <c>Grid</c> columns because the card is fluid, so the segments
/// have to be a fraction of whatever width the grid hands out, and a GridLength cannot be
/// bound from a double without a converter.
/// </para>
/// </summary>
public sealed class SegmentBar : Control
{
    public static readonly StyledProperty<double> CompletedFractionProperty =
        AvaloniaProperty.Register<SegmentBar, double>(nameof(CompletedFraction));

    public static readonly StyledProperty<double> FailingFractionProperty =
        AvaloniaProperty.Register<SegmentBar, double>(nameof(FailingFraction));

    /// <summary>False paints the completed share amber, so the bar is only green once the station is.</summary>
    public static readonly StyledProperty<bool> IsHealthyProperty =
        AvaloniaProperty.Register<SegmentBar, bool>(nameof(IsHealthy), true);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<SegmentBar, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> OkBrushProperty =
        AvaloniaProperty.Register<SegmentBar, IBrush?>(nameof(OkBrush));

    public static readonly StyledProperty<IBrush?> WaitBrushProperty =
        AvaloniaProperty.Register<SegmentBar, IBrush?>(nameof(WaitBrush));

    public static readonly StyledProperty<IBrush?> FailBrushProperty =
        AvaloniaProperty.Register<SegmentBar, IBrush?>(nameof(FailBrush));

    static SegmentBar()
    {
        AffectsRender<SegmentBar>(
            CompletedFractionProperty, FailingFractionProperty, IsHealthyProperty,
            TrackBrushProperty, OkBrushProperty, WaitBrushProperty, FailBrushProperty);
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
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var radius = height / 2;

        if (TrackBrush is { } track)
        {
            context.DrawRectangle(track, null, new RoundedRect(new Rect(0, 0, width, height), radius));
        }

        var completed = Math.Clamp(CompletedFraction, 0, 1);
        var failing = Math.Clamp(FailingFraction, 0, 1 - completed);

        if (completed > 0 && (IsHealthy ? OkBrush : WaitBrush) is { } fill)
        {
            context.DrawRectangle(fill, null, new RoundedRect(new Rect(0, 0, width * completed, height), radius));
        }

        if (failing > 0 && FailBrush is { } fail)
        {
            context.DrawRectangle(
                fail,
                null,
                new RoundedRect(new Rect(width * completed, 0, width * failing, height), radius));
        }
    }
}
