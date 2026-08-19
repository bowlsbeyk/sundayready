using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SundayReady.Services;
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
        if (_dragging is not { } device || Workspace is not { } workspace)
        {
            return;
        }

        var position = e.GetPosition(GraphSurface);
        var (x, y) = workspace.SnapPosition(
            device,
            Math.Max(0, position.X - _dragOffset.X),
            Math.Max(0, position.Y - _dragOffset.Y));
        device.X = x;
        device.Y = y;
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

    // ---------------------------------------------------------------- import / export

    /// <summary>
    /// The integrator hand-off, outbound. A file dialog because the destination is a USB stick
    /// or an email attachment, not the maps folder — the maps folder is already shared.
    /// </summary>
    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (Workspace is not { Current: { } map } workspace || StorageProvider is not { } storage)
        {
            return;
        }

        var file = await storage.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "Export map",
            SuggestedFileName = SystemMapStore.NewId(map.Name) + ".sundayready.json",
            FileTypeChoices = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("SundayReady map")
                {
                    Patterns = new[] { "*.sundayready.json", "*.json" },
                },
            },
        });

        if (file?.TryGetLocalPath() is { } path && workspace.ExportMap(path) is { } error)
        {
            workspace.Status = error;
        }
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (Workspace is not { } workspace || StorageProvider is not { } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Import map",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("SundayReady map")
                {
                    Patterns = new[] { "*.json" },
                },
            },
        });

        if (files.Count == 1 && files[0].TryGetLocalPath() is { } path
            && workspace.ImportMap(path) is { } error)
        {
            workspace.Status = error;
        }
    }

    private async void OnExportTemplatesClick(object? sender, RoutedEventArgs e)
    {
        if (Workspace is not { } workspace || StorageProvider is not { } storage)
        {
            return;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export template library",
            SuggestedFileName = "device-templates.sundayready.json",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("SundayReady templates")
                {
                    Patterns = new[] { "*.sundayready.json", "*.json" },
                },
            },
        });

        if (file?.TryGetLocalPath() is { } path && workspace.ExportTemplates(path) is { } error)
        {
            workspace.Status = error;
        }
    }

    private async void OnImportTemplatesClick(object? sender, RoutedEventArgs e)
    {
        if (Workspace is not { } workspace || StorageProvider is not { } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import templates",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("SundayReady templates") { Patterns = new[] { "*.json" } },
            },
        });

        if (files.Count == 1 && files[0].TryGetLocalPath() is { } path
            && workspace.ImportTemplates(path) is { } error)
        {
            workspace.Status = error;
        }
    }

    // ---------------------------------------------------------------- ports

    /// <summary>
    /// A click on a socket. While a run is armed this lands it; otherwise it starts one.
    /// <para>
    /// The direction menu only appears when the socket genuinely leaves it open. An output can only
    /// send and an input can only receive, so for those the click just arms — asking a question the
    /// data already answers is how a two-click gesture becomes a four-click one.
    /// </para>
    /// </summary>
    private void OnPortPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: MapPortAnchor anchor } control
            || Workspace is not { IsEditing: true } workspace)
        {
            return;
        }

        if (control.FindLogicalAncestorOfType<ItemsControl>()?.DataContext
            is not MapDeviceViewModel device)
        {
            return;
        }

        e.Handled = true;

        if (workspace.IsWiring)
        {
            workspace.FinishWireAtPort(device, anchor.PortId);
            return;
        }

        workspace.Select(device);

        var choices = new List<(string Label, string Mode)>();

        if (anchor.CanSend)
        {
            choices.Add(("Connect from here  →", MapWorkspaceViewModel.WireModes.From));
        }

        if (anchor.CanReceive)
        {
            choices.Add(("←  Connect to here", MapWorkspaceViewModel.WireModes.To));
        }

        if (anchor.IsAmbiguous)
        {
            choices.Add(("Connect both ways  ↔", MapWorkspaceViewModel.WireModes.Both));
        }

        if (choices.Count == 0)
        {
            return;
        }

        if (choices.Count == 1)
        {
            workspace.BeginPortWire(device, anchor.PortId, choices[0].Mode);
            return;
        }

        var menu = new ContextMenu
        {
            ItemsSource = choices
                .Select(choice =>
                {
                    var item = new MenuItem { Header = choice.Label };
                    item.Click += (_, _) =>
                        workspace.BeginPortWire(device, anchor.PortId, choice.Mode);
                    return item;
                })
                .ToList(),
        };

        menu.Open(control);
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
        var snap = Workspace?.SnapEnabled == true;
        var x = Math.Max(0, position.X - _noteOffset.X);
        var y = Math.Max(0, position.Y - _noteOffset.Y);
        note.X = snap ? Math.Round(x / 10) * 10 : x;
        note.Y = snap ? Math.Round(y / 10) * 10 : y;
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
