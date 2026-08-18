using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SundayReady.ViewModels;

namespace SundayReady.Views;

/// <summary>
/// The system map window. Owns only what a view must: the clock, pointer gestures, and moving
/// the spotlight of selection — everything it learns goes straight to the workspace view model.
/// </summary>
public partial class MapWindow : Window
{
    private DispatcherTimer? _clock;

    private MapDeviceViewModel? _dragging;
    private Avalonia.Point _dragOffset;
    private bool _dragMoved;

    /// <summary>How wide the wire-from-here strip is at each end of a box.</summary>
    private const double EdgeZone = 26;

    public MapWindow()
    {
        // The generated InitializeComponent, deliberately — a hand-written one that only calls
        // AvaloniaXamlLoader.Load leaves every x:Name field null. That bit twice already.
        InitializeComponent();

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => Clock.Text = DateTime.Now.ToString("h:mm");
        _clock.Start();
        Clock.Text = DateTime.Now.ToString("h:mm");

        Closed += (_, _) =>
        {
            _clock?.Stop();
            _clock = null;

            if (DataContext is MapWorkspaceViewModel workspace)
            {
                workspace.Dispose();
            }
        };
    }

    private MapWorkspaceViewModel? Workspace => DataContext as MapWorkspaceViewModel;

    // ---------------------------------------------------------------- nodes

    /// <summary>
    /// A press on a box. While editing, the left and right thirds of a box start a wire and the
    /// middle moves it — which is what the footnote has always promised ("drag from an edge to
    /// wire it") and what the previous build did not actually do. Wiring by drag matters more
    /// than the button did: nobody finds a two-step select-then-press gesture on a diagram.
    /// </summary>
    private void OnNodePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: MapDeviceViewModel device } control
            || Workspace is not { } workspace)
        {
            return;
        }

        // A wire already in flight lands here.
        if (workspace.IsWiring)
        {
            workspace.FinishWire(device);
            e.Handled = true;
            return;
        }

        workspace.Select(device);

        if (workspace.IsEditing)
        {
            var local = e.GetPosition(control);
            var onEdge = local.X <= EdgeZone || local.X >= MapDeviceViewModel.BoxWidth - EdgeZone;

            if (onEdge)
            {
                workspace.BeginWireFrom(device);
                e.Pointer.Capture(control);
            }
            else
            {
                var position = e.GetPosition(GraphSurface);
                _dragging = device;
                _dragOffset = new Avalonia.Point(position.X - device.X, position.Y - device.Y);
                _dragMoved = false;
                e.Pointer.Capture(control);
            }
        }

        e.Handled = true;
    }

    private void OnNodeMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging is not { } device)
        {
            return;
        }

        var position = e.GetPosition(GraphSurface);
        device.X = Math.Max(0, position.X - _dragOffset.X);
        device.Y = Math.Max(0, position.Y - _dragOffset.Y);
        _dragMoved = true;
    }

    private void OnNodeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Workspace is { IsWiring: true } workspace)
        {
            // Released over a box: wire to it. Released anywhere else: the wire is still armed,
            // so a click on the target finishes it — which is what a slip of the hand needs.
            e.Pointer.Capture(null);
            var target = workspace.DeviceAt(e.GetPosition(GraphSurface));

            if (target is not null && !ReferenceEquals(target, workspace.WireFrom))
            {
                workspace.FinishWire(target);
            }

            return;
        }

        if (_dragging is null)
        {
            return;
        }

        e.Pointer.Capture(null);
        _dragging = null;

        if (_dragMoved)
        {
            Workspace?.Current?.RefreshExtent();
        }
    }

    private void OnNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: MapDeviceViewModel device }
            || Workspace is not { Current: { } map } workspace)
        {
            return;
        }

        // A linked box drills into its map; anything else opens the inspector, so a
        // double-click always answers "tell me more about this".
        if (!workspace.Drill(device))
        {
            new MapInspectorWindow
            {
                DataContext = new MapInspectorViewModel(workspace, map, device),
            }.Show(this);
        }

        e.Handled = true;
    }

    // ---------------------------------------------------------------- surface

    /// <summary>A press that no node claimed: try the wires, then clear the selection.</summary>
    private void OnSurfacePressed(object? sender, PointerPressedEventArgs e)
    {
        if (Workspace is not { } workspace)
        {
            return;
        }

        if (workspace.IsWiring)
        {
            workspace.CancelWireCommand.Execute(null);
            return;
        }

        var hit = Wires.HitTest(e.GetPosition(Wires));
        if (hit is not null)
        {
            workspace.Select(hit);
            return;
        }

        workspace.ClearSelection();
    }

    // ---------------------------------------------------------------- rail & top bar

    private void OnLegendRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: MapLegendRowViewModel row }
            && Workspace is { } workspace)
        {
            workspace.ToggleIsolate(row);
        }
    }

    private void OnOpenLinkedClick(object? sender, RoutedEventArgs e)
    {
        if (Workspace is { SelectedDevice: { } device } workspace)
        {
            workspace.Drill(device);
        }
    }

    private void OnInspectClick(object? sender, RoutedEventArgs e)
    {
        if (Workspace is not { SelectedDevice: { } device, Current: { } map } workspace)
        {
            return;
        }

        new MapInspectorWindow
        {
            DataContext = new MapInspectorViewModel(workspace, map, device),
        }.Show(this);
    }

    private void OnTypesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Workspace is not { } workspace)
        {
            return;
        }

        var window = new MapTypesWindow
        {
            DataContext = new MapTypeRegistryViewModel(workspace.Store, workspace),
        };
        window.Show(this);
    }

    /// <summary>Drops a new device in the top-left of the current viewport, ready to drag.</summary>
    private void OnAddDeviceClick(object? sender, RoutedEventArgs e)
    {
        if (Workspace is not { } workspace)
        {
            return;
        }

        var origin = CanvasScroll.Offset;
        workspace.AddDevice(origin.X + 80, origin.Y + 80);
    }

    private void OnAddNoteClick(object? sender, RoutedEventArgs e)
    {
        if (Workspace is not { } workspace)
        {
            return;
        }

        // Offset from where a new device would land, so pressing both buttons does not stack two
        // things on one spot.
        var origin = CanvasScroll.Offset;
        workspace.AddNote(origin.X + 320, origin.Y + 80);
    }

    // ---------------------------------------------------------------- notes

    private MapNoteViewModel? _draggingNote;
    private Avalonia.Point _noteOffset;

    /// <summary>
    /// Notes drag from their border but not from their text, because the text is a live editor and
    /// stealing the pointer from it would make a note impossible to type into.
    /// </summary>
    private void OnNotePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: MapNoteViewModel note } control
            || Workspace is not { } workspace)
        {
            return;
        }

        workspace.SelectNote(note);

        if (!workspace.IsEditing || e.Source is TextBox)
        {
            e.Handled = true;
            return;
        }

        var position = e.GetPosition(GraphSurface);
        _draggingNote = note;
        _noteOffset = new Avalonia.Point(position.X - note.X, position.Y - note.Y);
        e.Pointer.Capture(control);
        e.Handled = true;
    }

    private void OnNoteMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingNote is not { } note)
        {
            return;
        }

        var position = e.GetPosition(GraphSurface);
        note.X = Math.Max(0, position.X - _noteOffset.X);
        note.Y = Math.Max(0, position.Y - _noteOffset.Y);
    }

    private void OnNoteReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggingNote is null)
        {
            return;
        }

        e.Pointer.Capture(null);
        _draggingNote.Commit();
        _draggingNote = null;
        Workspace?.Current?.RefreshExtent();
    }
}
