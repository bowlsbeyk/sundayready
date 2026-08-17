using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SundayReady.Services;
using SundayReady.ViewModels;

namespace SundayReady.Views;

/// <summary>
/// Runs the guided tour across windows.
/// <para>
/// The tour crosses from the station to the editor and back, so no single window can own it.
/// Each participating window registers the overlay it contains and which surface it is; the host
/// keeps one <see cref="TourViewModel"/> and, on every step, shows the overlay belonging to that
/// step's surface and hides the rest. A window that opens mid-tour — the editor, which is exactly
/// what the tour asks you to open — picks the tour up when it registers.
/// </para>
/// </summary>
public static class TourHost
{
    private static readonly List<Registration> Registered = new();

    private static TourViewModel? _tour;

    /// <summary>True while a tour is running, so entry points can offer to resume rather than restart.</summary>
    public static bool IsRunning => _tour is { IsRunning: true };

    /// <summary>Starts a tour, replacing any that was already running.</summary>
    public static void Start()
    {
        Stop();

        var tour = new TourViewModel();
        _tour = tour;

        tour.Moved += (_, _) => Refresh();
        tour.Ended += (_, _) =>
        {
            _tour = null;
            Refresh();
        };

        Refresh();
    }

    public static void Stop()
    {
        if (_tour is null)
        {
            return;
        }

        _tour = null;
        Refresh();
    }

    /// <summary>
    /// Called by a window that can host tour steps. Safe to call for a window that opens long
    /// before or during a tour.
    /// </summary>
    public static void Register(Window window, TourSurface surface, TourOverlay? overlay)
    {
        if (overlay is null)
        {
            // Only happens when a window declares its own InitializeComponent, which stops the
            // generated one from assigning the x:Name fields. Skipping the window costs a tour
            // step; dereferencing it costs the whole app.
            UpdateInstaller.Log($"tour: {window.GetType().Name} registered without an overlay");
            return;
        }

        Registered.RemoveAll(r => ReferenceEquals(r.Window, window));
        var registration = new Registration(window, surface, overlay);
        Registered.Add(registration);

        window.Closed += (_, _) =>
        {
            registration.Detach();
            Registered.Remove(registration);

            // Closing the editor mid-tour is a legitimate way to carry on: the next station step
            // is waiting behind it.
            Refresh();
        };

        Refresh();
    }

    /// <summary>Re-points every overlay at whatever the current step wants.</summary>
    private static void Refresh()
    {
        var tour = _tour;

        foreach (var registration in Registered.ToList())
        {
            registration.Detach();

            if (tour is not { IsRunning: true })
            {
                registration.Overlay.IsVisible = false;
                registration.Overlay.DataContext = null;
                continue;
            }

            registration.Overlay.DataContext = tour;
        }

        if (tour is not { IsRunning: true })
        {
            return;
        }

        // Skip steps whose surface has no window open and no control to point at. A station with
        // no items has nothing for the "the list itself" step to highlight.
        if (!tour.SkipWhile(step => !CanShow(step)))
        {
            return;
        }

        var current = tour.Step;

        foreach (var registration in Registered)
        {
            var mine = registration.Surface == current.Surface;
            registration.Overlay.IsVisible = mine;

            if (!mine)
            {
                continue;
            }

            var target = Find(registration.Window, current.Target);
            registration.Overlay.PointAt(target, current.Placement);

            if (target is not null && current.Advance == TourAdvance.Click)
            {
                registration.AttachClick(target, tour);
            }
        }
    }

    /// <summary>
    /// Whether some open window can actually show this step. A step for a window that is not
    /// open is not skippable — the tour is about to ask the person to open it — so only a
    /// missing control in an open window counts as unshowable.
    /// </summary>
    private static bool CanShow(TourStep step)
    {
        var hosts = Registered.Where(r => r.Surface == step.Surface).ToList();
        if (hosts.Count == 0)
        {
            // The editor is not open yet. That is expected right up until the step that asks for
            // it, and the station steps around it still work, so keep the step.
            return true;
        }

        return hosts.Any(r => Find(r.Window, step.Target) is not null);
    }

    /// <summary>
    /// Finds a control by name anywhere in the window.
    /// <para>
    /// The visual tree rather than <c>FindControl</c>, because the controls being pointed at live
    /// inside <c>StationView</c> and templated items, each of which is its own name scope — a
    /// lookup from the window would find none of them.
    /// </para>
    /// </summary>
    private static Control? Find(Window window, string name) =>
        window.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(c => c.Name == name && c.IsVisible && c.Bounds.Width > 0);

    private sealed class Registration
    {
        private Control? _clickTarget;
        private EventHandler<RoutedEventArgs>? _buttonHandler;
        private EventHandler<PointerReleasedEventArgs>? _pointerHandler;

        public Registration(Window window, TourSurface surface, TourOverlay overlay)
        {
            Window = window;
            Surface = surface;
            Overlay = overlay;
        }

        public Window Window { get; }

        public TourSurface Surface { get; }

        public TourOverlay Overlay { get; }

        /// <summary>
        /// Advances the tour when the person presses the real control. Buttons expose Click,
        /// which fires after the command has run; anything else falls back to the pointer, which
        /// is close enough for a text box or a list.
        /// </summary>
        public void AttachClick(Control target, TourViewModel tour)
        {
            _clickTarget = target;

            if (target is Button button)
            {
                _buttonHandler = (_, _) => tour.TargetClicked();
                button.Click += _buttonHandler;
                return;
            }

            _pointerHandler = (_, _) => tour.TargetClicked();
            target.PointerReleased += _pointerHandler;
        }

        public void Detach()
        {
            if (_clickTarget is Button button && _buttonHandler is not null)
            {
                button.Click -= _buttonHandler;
            }
            else if (_clickTarget is not null && _pointerHandler is not null)
            {
                _clickTarget.PointerReleased -= _pointerHandler;
            }

            _clickTarget = null;
            _buttonHandler = null;
            _pointerHandler = null;
        }
    }
}
