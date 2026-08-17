using Avalonia;
using Avalonia.Controls;
using SundayReady.Services;

namespace SundayReady.Views;

/// <summary>
/// The spotlight. Given a control and a placement it dims everything except that control and puts
/// a callout beside it.
/// <para>
/// Positioning is done here rather than in XAML because it depends on where the target actually
/// ended up, which is only known after layout — and it has to be redone whenever the window
/// resizes or the target moves.
/// </para>
/// </summary>
public partial class TourOverlay : UserControl
{
    /// <summary>Breathing room around the highlighted control.</summary>
    private const double Pad = 6;

    /// <summary>Between the hole and the callout.</summary>
    private const double Gap = 14;

    /// <summary>Kept clear of the window edges so the callout never sits flush against one.</summary>
    private const double EdgeGap = 16;

    private Control? _target;
    private TourPlacement _placement = TourPlacement.Below;

    /// <summary>
    /// The arranged size, taken from ArrangeOverride's argument rather than read back off
    /// <see cref="Visual.Bounds"/>. Bounds is only updated after arrange finishes, so reading it
    /// from inside ArrangeOverride yields the previous pass — zero on the first one, which left
    /// the whole overlay computed against an empty rectangle and invisible.
    /// </summary>
    private Size _size;

    public TourOverlay()
    {
        // No hand-written InitializeComponent here, unlike the other views in this project.
        // Declaring one suppresses the generated version — and the generated version is what
        // assigns the x:Name fields. The XAML still loads, so nothing looks wrong until the
        // first line of code that touches one of those fields dereferences null.
        InitializeComponent();
    }

    /// <summary>Points the spotlight at a control. Null hides everything but leaves the tour running.</summary>
    public void PointAt(Control? target, TourPlacement placement)
    {
        _target = target;
        _placement = placement;
        Reposition();

        // The overlay is usually made visible in the same breath as this call, so it has not been
        // arranged yet and the target may not have either. One more pass once layout has settled
        // is what makes the spotlight land in the right place on the first step rather than the
        // second.
        Avalonia.Threading.Dispatcher.UIThread.Post(
            Reposition,
            Avalonia.Threading.DispatcherPriority.Loaded);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        _size = finalSize;

        // Children have just been arranged, so their own Bounds are current and the callout can
        // be placed against its real height.
        Reposition();
        return size;
    }

    private void Reposition()
    {
        if (_size.Width <= 0 || _size.Height <= 0)
        {
            return;
        }

        var w = _size.Width;
        var h = _size.Height;

        // Top bar first: it is the one thing on screen whatever the step is doing.
        Canvas.SetLeft(SkipBar, Math.Max(0, (w - SkipBar.Bounds.Width) / 2));
        Canvas.SetTop(SkipBar, 0);

        var hole = HoleFor(w, h);

        DimTop.Width = w;
        DimTop.Height = Math.Max(0, hole.Top);
        Canvas.SetLeft(DimTop, 0);
        Canvas.SetTop(DimTop, 0);

        DimBottom.Width = w;
        DimBottom.Height = Math.Max(0, h - hole.Bottom);
        Canvas.SetLeft(DimBottom, 0);
        Canvas.SetTop(DimBottom, hole.Bottom);

        DimLeft.Width = Math.Max(0, hole.Left);
        DimLeft.Height = Math.Max(0, hole.Height);
        Canvas.SetLeft(DimLeft, 0);
        Canvas.SetTop(DimLeft, hole.Top);

        DimRight.Width = Math.Max(0, w - hole.Right);
        DimRight.Height = Math.Max(0, hole.Height);
        Canvas.SetLeft(DimRight, hole.Right);
        Canvas.SetTop(DimRight, hole.Top);

        Ring.IsVisible = _target is not null;
        Ring.Width = hole.Width;
        Ring.Height = hole.Height;
        Canvas.SetLeft(Ring, hole.Left);
        Canvas.SetTop(Ring, hole.Top);

        PlaceCallout(hole, w, h);
    }

    /// <summary>
    /// The target's rectangle in this overlay's coordinates, padded. With no target — a step
    /// whose control is off screen — it collapses to nothing, so the whole window dims and the
    /// callout still reads.
    /// </summary>
    private Rect HoleFor(double w, double h)
    {
        if (_target is null || !_target.IsVisible || _target.Bounds.Width <= 0)
        {
            return new Rect(w / 2, 0, 0, 0);
        }

        var origin = _target.TranslatePoint(default, this);
        if (origin is not { } point)
        {
            return new Rect(w / 2, 0, 0, 0);
        }

        var rect = new Rect(
            point.X - Pad,
            point.Y - Pad,
            _target.Bounds.Width + (Pad * 2),
            _target.Bounds.Height + (Pad * 2));

        // A control scrolled half out of view would otherwise punch a hole through the window
        // edge; clamping keeps the dim panels non-negative and the ring on screen.
        var left = Math.Clamp(rect.Left, 0, w);
        var top = Math.Clamp(rect.Top, 0, h);
        var right = Math.Clamp(rect.Right, left, w);
        var bottom = Math.Clamp(rect.Bottom, top, h);

        return new Rect(left, top, right - left, bottom - top);
    }

    private void PlaceCallout(Rect hole, double w, double h)
    {
        var size = Callout.Bounds.Size;
        if (size.Width <= 0)
        {
            // Not measured yet on the first pass; ArrangeOverride runs again with real numbers.
            size = new Size(380, 220);
        }

        double x;
        double y;

        switch (_placement)
        {
            case TourPlacement.Above:
                x = hole.Center.X - (size.Width / 2);
                y = hole.Top - size.Height - Gap;
                break;
            case TourPlacement.Left:
                x = hole.Left - size.Width - Gap;
                y = hole.Center.Y - (size.Height / 2);
                break;
            case TourPlacement.Right:
                x = hole.Right + Gap;
                y = hole.Center.Y - (size.Height / 2);
                break;
            default:
                x = hole.Center.X - (size.Width / 2);
                y = hole.Bottom + Gap;
                break;
        }

        // If the preferred side does not fit, flip to the opposite one before falling back to
        // clamping — a callout jammed against an edge overlapping its own target is unreadable.
        if (y + size.Height > h - EdgeGap && _placement == TourPlacement.Below)
        {
            y = hole.Top - size.Height - Gap;
        }
        else if (y < EdgeGap && _placement == TourPlacement.Above)
        {
            y = hole.Bottom + Gap;
        }

        if (x + size.Width > w - EdgeGap && _placement == TourPlacement.Right)
        {
            x = hole.Left - size.Width - Gap;
        }
        else if (x < EdgeGap && _placement == TourPlacement.Left)
        {
            x = hole.Right + Gap;
        }

        Canvas.SetLeft(Callout, Math.Clamp(x, EdgeGap, Math.Max(EdgeGap, w - size.Width - EdgeGap)));
        Canvas.SetTop(Callout, Math.Clamp(y, EdgeGap, Math.Max(EdgeGap, h - size.Height - EdgeGap)));
    }
}
