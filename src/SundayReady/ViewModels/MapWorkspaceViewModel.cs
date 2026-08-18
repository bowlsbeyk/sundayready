using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SundayReady.Models;
using SundayReady.Services;

namespace SundayReady.ViewModels;

/// <summary>One loaded map: its devices, its wires, and how it is doing as a whole.</summary>
public sealed partial class SystemMapViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HealthLabel), nameof(IsHealthy), nameof(IsBroken), nameof(LinksDownLabel))]
    private VerifyStatus _health = VerifyStatus.Unknown;

    public SystemMapViewModel(SystemMap model, VerifierRegistry registry, IReadOnlyList<MapConnectionType> types)
    {
        Model = model;

        MapConnectionType Resolve(string? id) =>
            types.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? MapConnectionTypes.Unknown;

        var byId = new Dictionary<string, MapDeviceViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in model.Devices)
        {
            var vm = new MapDeviceViewModel(
                device,
                registry,
                device.DominantType is null ? null : Resolve(device.DominantType));
            Devices.Add(vm);
            byId[vm.Id] = vm;
        }

        foreach (var connection in model.Connections)
        {
            // A connection naming a device that is not on the map is dropped rather than drawn
            // to nowhere. Hand-edited files get this wrong, and half a line is worse than none.
            if (byId.TryGetValue(connection.From, out var from) && byId.TryGetValue(connection.To, out var to))
            {
                Connections.Add(new MapConnectionViewModel(
                    connection, from, to, Resolve(connection.Type), registry));
            }
        }

        foreach (var note in model.Notes)
        {
            Notes.Add(new MapNoteViewModel(
                note,
                note.AboutDevice is { Length: > 0 } id && byId.TryGetValue(id, out var about)
                    ? about
                    : null));
        }

        AssignPortSlots();
    }

    /// <summary>
    /// Spreads each device's wires along its edges so a heavily-patched box stays readable, and
    /// anchors the ones that name a <see cref="MapPort"/> to that port instead.
    /// </summary>
    private void AssignPortSlots()
    {
        foreach (var device in Devices)
        {
            // Which sockets are carrying anything, anywhere on this box. Worked out once and for
            // both edges, because a both-ways socket in use on the left is not a free socket on
            // the right — drawing it as one would advertise a jack that is already occupied.
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var connection in Connections)
            {
                if (ReferenceEquals(connection.From, device) && connection.Model.FromPort is { } f)
                {
                    used.Add(f);
                }

                if (ReferenceEquals(connection.To, device) && connection.Model.ToPort is { } t)
                {
                    used.Add(t);
                }
            }

            AssignEdge(device, rightSide: true, used);
            AssignEdge(device, rightSide: false, used);
        }

        foreach (var connection in Connections)
        {
            connection.RefreshGeometry();
        }
    }

    /// <summary>
    /// Positions everything landing on one edge of one box.
    /// <para>
    /// Each edge is fanned separately, which matters more than it sounds: a box in the middle of
    /// the map has wires leaving rightwards and wires leaving leftwards, and numbering them as one
    /// sequence spreads two half-empty edges instead of two full ones.
    /// </para>
    /// <para>
    /// Wires naming a port collapse onto that port's anchor — one socket is one point, however many
    /// runs claim it, because two cables in one XLR jack is a fact worth seeing rather than a
    /// drawing to tidy away. Declared ports sit in the order the author listed them; everything
    /// unnamed falls below, sorted by where its far end sits so the fan does not cross itself.
    /// </para>
    /// </summary>
    private void AssignEdge(MapDeviceViewModel device, bool rightSide, ISet<string> usedPorts)
    {
        var ends = new List<(MapConnectionViewModel Wire, bool FromEnd, string? PortId, double FarY)>();

        foreach (var connection in Connections)
        {
            var forward = connection.To.Centre.X >= connection.From.Centre.X;

            if (ReferenceEquals(connection.From, device) && forward == rightSide)
            {
                ends.Add((connection, true, connection.Model.FromPort, connection.To.Centre.Y));
            }

            if (ReferenceEquals(connection.To, device) && forward != rightSide)
            {
                ends.Add((connection, false, connection.Model.ToPort, connection.From.Centre.Y));
            }
        }

        var declared = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < device.Model.Ports.Count; i++)
        {
            declared[device.Model.Ports[i].Id] = i;
        }

        string KeyFor(string? portId) =>
            portId is { Length: > 0 } id && declared.ContainsKey(id) ? "port:" + id : null!;

        // Declared ports hold a slot on their edge whether or not anything is plugged in. Without
        // this an empty socket would have nowhere to be drawn — and you cannot click a socket to
        // wire it if it is invisible until something is already wired to it. It also keeps ports
        // from jumping about the moment a wire is added or removed.
        var vacant = device.Model.Ports
            .Where(p => !usedPorts.Contains(p.Id))
            .Where(p => rightSide ? MapPortSides.AcceptsOut(p.Side) : p.Side == MapPortSides.In)
            .ToList();

        if (ends.Count == 0 && vacant.Count == 0)
        {
            device.SetPortAnchors(rightSide, Array.Empty<MapPortAnchor>());
            return;
        }

        var groups = ends
            .GroupBy(e => KeyFor(e.PortId) ?? "wire:" + e.Wire.Model.Id + (e.FromEnd ? ":a" : ":b"))
            .Select(g => new
            {
                Ends = g.ToList(),
                PortId = KeyFor(g.First().PortId) is null ? null : g.First().PortId,
                Order = KeyFor(g.First().PortId) is null
                    ? (int?)null
                    : declared[g.First().PortId!],
                FarY = g.Average(e => e.FarY),
            })
            .Concat(vacant.Select(p => new
            {
                Ends = new List<(MapConnectionViewModel Wire, bool FromEnd, string? PortId, double FarY)>(),
                PortId = (string?)p.Id,
                Order = (int?)declared[p.Id],
                FarY = 0d,
            }))
            .OrderBy(g => g.Order is null ? 1 : 0)
            .ThenBy(g => g.Order ?? 0)
            .ThenBy(g => g.FarY)
            .ToList();

        var anchors = new List<MapPortAnchor>();

        for (var i = 0; i < groups.Count; i++)
        {
            var slot = Slot(i, groups.Count);

            foreach (var end in groups[i].Ends)
            {
                if (end.FromEnd)
                {
                    end.Wire.FromSlot = slot;
                }
                else
                {
                    end.Wire.ToSlot = slot;
                }
            }

            if (groups[i].PortId is { } portId)
            {
                var spec = device.Model.Ports.FirstOrDefault(p => p.Id == portId);
                anchors.Add(new MapPortAnchor(
                    portId,
                    spec?.Label ?? portId,
                    slot,
                    groups[i].Ends.Count,
                    spec?.Side ?? MapPortSides.Both,
                    rightSide));
            }
        }

        device.SetPortAnchors(rightSide, anchors);
    }

    /// <summary>One wire sits at the centre; several spread evenly across the edge.</summary>
    private static double Slot(int index, int count) =>
        count <= 1 ? 0.5 : (index + 0.5) / count;

    /// <summary>Re-fans after anything that changes the wiring or the layout.</summary>
    public void RefreshPorts() => AssignPortSlots();

    public SystemMap Model { get; }

    public string FileName => Model.SourceFile;

    public string Name => Model.Name;

    public string? Summary => Model.Summary;

    public bool HasSummary => !string.IsNullOrWhiteSpace(Model.Summary);

    public ObservableCollection<MapDeviceViewModel> Devices { get; } = new();

    public ObservableCollection<MapConnectionViewModel> Connections { get; } = new();

    public ObservableCollection<MapNoteViewModel> Notes { get; } = new();

    public IReadOnlyList<MapColumn> Columns => Model.Columns;

    public IReadOnlyList<MapColumnBand> ColumnBands =>
        Model.Columns.Select(c => new MapColumnBand(c.Label, c.X, CanvasHeight - 130)).ToList();

    public IEnumerable<MapProbeViewModel> Probes =>
        Devices.Cast<MapProbeViewModel>().Concat(Connections);

    public bool IsHealthy => Health == VerifyStatus.Passed;

    public bool IsBroken => Health is VerifyStatus.Failed or VerifyStatus.Unsupported;

    public string HealthLabel => Health switch
    {
        VerifyStatus.Passed => "All checks passing",
        VerifyStatus.Polling => "Checking…",
        VerifyStatus.Failed => $"{Failing.Count()} not passing",
        VerifyStatus.Unsupported => "A check on this map is not recognised",
        _ => "Nothing on this map is checked",
    };

    /// <summary>The top bar's pill: <c>1 OF 34 LINKS DOWN</c>. Empty when everything flows.</summary>
    public string LinksDownLabel
    {
        get
        {
            var down = Connections.Count(c => c.IsDown);
            return down == 0 ? string.Empty : $"{down} OF {Connections.Count} LINKS DOWN";
        }
    }

    public bool HasLinksDown => Connections.Any(c => c.IsDown);

    public IEnumerable<MapProbeViewModel> Failing =>
        Probes.Where(p => p.IsFailed && p is not MapDeviceViewModel { IsReported: true });

    public double CanvasWidth => Math.Max(
        1500,
        Math.Max(
            Devices.Count == 0 ? 0 : Devices.Max(d => d.X) + MapDeviceViewModel.BoxWidth + 120,
            Notes.Count == 0 ? 0 : Notes.Max(n => n.X) + MapNoteViewModel.NoteWidth + 120));

    public double CanvasHeight => Math.Max(
        900,
        Math.Max(
            Devices.Count == 0 ? 0 : Devices.Max(d => d.Y) + MapDeviceViewModel.BoxHeight + 120,
            Notes.Count == 0 ? 0 : Notes.Max(n => n.Y) + 160));

    public void RefreshExtent()
    {
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        OnPropertyChanged(nameof(ColumnBands));

        // Moving a box changes which order its wires should fan in.
        AssignPortSlots();
    }

    public void RefreshHealthDetail()
    {
        OnPropertyChanged(nameof(LinksDownLabel));
        OnPropertyChanged(nameof(HasLinksDown));
    }

    /// <summary>
    /// This map's own health, honouring the gate rule: only devices that can hold the gate count
    /// against it, plus connection checks between on-campus devices. Reported and inferred nodes
    /// never make a map "broken" — off-campus trouble is a banner, not a red map.
    /// </summary>
    public VerifyStatus OwnHealth()
    {
        var gating = new List<VerifyStatus>();

        foreach (var device in Devices)
        {
            if (device.CanHoldGate && device.HasVerify)
            {
                gating.Add(device.Status);
            }

            if (device.LinkedStatus != VerifyStatus.Unknown)
            {
                gating.Add(device.LinkedStatus);
            }
        }

        foreach (var connection in Connections.Where(c =>
                     c.HasVerify && !c.From.OffCampus && !c.To.OffCampus))
        {
            gating.Add(connection.Status);
        }

        if (gating.Count == 0)
        {
            return VerifyStatus.Unknown;
        }

        var worst = VerifyStatus.Passed;
        foreach (var status in gating)
        {
            worst = MapProbeViewModel.Worst(worst, status);
        }

        return worst;
    }
}

/// <summary>A role-column band as the canvas draws it: label, position, and a height that
/// tracks the canvas so the band always reaches the bottom of the drawing.</summary>
public sealed record MapColumnBand(string Label, double X, double Height);

/// <summary>One legend row: a type, its look, and how many wires on the open map use it.</summary>
public sealed partial class MapLegendRowViewModel : ObservableObject
{
    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private bool _isIsolated;

    public MapLegendRowViewModel(MapConnectionType type)
    {
        Type = type;
    }

    public MapConnectionType Type { get; }

    public string Name => Type.Name;
}

/// <summary>
/// Every map the church has, and the one currently on screen.
/// <para>
/// All maps are polled, not just the visible one. That is the whole point of nesting: a device on
/// the top-level map goes red because something a level down did, and it can only do that if the
/// levels below are being checked while nobody is looking at them.
/// </para>
/// </summary>
public sealed partial class MapWorkspaceViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly SystemMapStore _store;
    private readonly VerifierRegistry _registry;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly List<SystemMapViewModel> _maps = new();
    private readonly Stack<SystemMapViewModel> _back = new();

    private IReadOnlyList<MapConnectionType> _types = MapConnectionTypes.BuiltIn;
    private DispatcherTimer? _timer;
    private bool _polling;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrent), nameof(CanGoBack), nameof(Title), nameof(IsEmpty))]
    private SystemMapViewModel? _current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private MapDeviceViewModel? _selectedDevice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(HasSelectedConnection))]
    private MapConnectionViewModel? _selectedConnection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeLabel))]
    private bool _isEditing;

    /// <summary>The device a wire is being dragged from, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWiring), nameof(ModeLabel))]
    private MapDeviceViewModel? _wireFrom;

    /// <summary>The legend's isolate filter: a type id, or null for everything.</summary>
    [ObservableProperty]
    private string? _isolatedType;

    /// <summary>The SHOW chips: null (all) | video | audio | lighting | faults.</summary>
    [ObservableProperty]
    private string? _categoryFilter;

    /// <summary>Editable fields for the selected device, present only while editing.</summary>
    [ObservableProperty]
    private MapDeviceEditorViewModel? _deviceEditor;

    [ObservableProperty]
    private MapConnectionEditorViewModel? _connectionEditor;

    /// <summary>The empty state's map-name box.</summary>
    [ObservableProperty]
    private string _newMapName = string.Empty;

    /// <summary>Wall displays run for hours; a slow booth PC can turn the motion off entirely.</summary>
    [ObservableProperty]
    private bool _freezeWires;

    [ObservableProperty]
    private string _status = string.Empty;

    public MapWorkspaceViewModel(SystemMapStore store, VerifierRegistry registry)
    {
        _store = store;
        _registry = registry;
        Load();
    }

    public ObservableCollection<SystemMapViewModel> Maps { get; } = new();

    public ObservableCollection<MapLegendRowViewModel> Legend { get; } = new();

    public ObservableCollection<string> MapFiles { get; } = new();

    public IReadOnlyList<MapConnectionType> Types => _types;

    /// <summary>The store, for surfaces that share it — the type registry window.</summary>
    public SystemMapStore Store => _store;

    public bool HasCurrent => Current is not null;

    public bool IsEmpty => Current is null;

    public bool CanGoBack => _back.Count > 0;

    public bool HasSelection => SelectedDevice is not null || SelectedConnection is not null;

    public bool HasSelectedConnection => SelectedConnection is not null;

    public bool IsWiring => WireFrom is not null;

    public string Title => Current?.Name ?? "System map";

    public string MapsFolder => _store.Directory;

    public string ModeLabel => IsWiring
        ? _wireMode switch
        {
            WireModes.To => $"Wiring into {WireFrom?.Label} — click what feeds it",
            WireModes.Both => $"Wiring {WireFrom?.Label} both ways — click the other end",
            _ => $"Wiring from {WireFrom?.Label} — click where it lands",
        }
        : IsEditing
            ? "Editing — drag a box's middle to move it, drag from either end to wire it, "
                + "or click a socket to wire that socket"
            : string.Empty;

    public void Start()
    {
        if (_timer is not null || _disposed)
        {
            return;
        }

        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += (_, _) => _ = PollAsync();
        _timer.Start();
        _ = PollAsync();
    }

    /// <summary>Re-reads the registry and every map from disk, keeping the current one open.</summary>
    public void Load()
    {
        var openFile = Current?.FileName;

        _types = _store.LoadTypes();
        _maps.Clear();
        Maps.Clear();
        MapFiles.Clear();
        _back.Clear();

        var errors = new List<string>();

        foreach (var file in _store.ListFiles())
        {
            try
            {
                var map = new SystemMapViewModel(_store.Load(file), _registry, _types);
                _maps.Add(map);
                Maps.Add(map);
                MapFiles.Add(file);
            }
            catch (ChecklistLoadException ex)
            {
                errors.Add(ex.Message);
            }
            catch (Exception ex)
            {
                errors.Add($"{file} — {ex.Message}");
            }
        }

        Current = _maps.FirstOrDefault(m => m.FileName == openFile) ?? _maps.FirstOrDefault();
        SelectedDevice = null;
        SelectedConnection = null;

        // Everything came off disk again, so the history describes states that no longer exist.
        ForgetUndo();

        Status = errors.Count > 0
            ? string.Join(Environment.NewLine, errors)
            : _maps.Count == 0
                ? $"No maps yet. They live in {_store.Directory}."
                : string.Empty;

        OnPropertyChanged(nameof(CanGoBack));
        RebuildLegend();
        RollUp();
    }

    public void Open(SystemMapViewModel map, bool remember = true)
    {
        if (Current is { } current && remember && !ReferenceEquals(current, map))
        {
            _back.Push(current);
        }

        Current = map;
        SelectedDevice = null;
        SelectedConnection = null;
        OnPropertyChanged(nameof(CanGoBack));
        RebuildLegend();
    }

    [RelayCommand]
    private void Back()
    {
        if (_back.Count == 0)
        {
            return;
        }

        Current = _back.Pop();
        SelectedDevice = null;
        SelectedConnection = null;
        OnPropertyChanged(nameof(CanGoBack));
        RebuildLegend();
    }

    /// <summary>Follows a device's link, if it has one.</summary>
    public bool Drill(MapDeviceViewModel device)
    {
        if (!device.HasLink)
        {
            return false;
        }

        var target = _maps.FirstOrDefault(m =>
            string.Equals(m.FileName, device.LinksTo, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            Status = $"“{device.Label}” links to {device.LinksTo}, which is not in {_store.Directory}.";
            return false;
        }

        Open(target);
        return true;
    }

    /// <summary>Selects a device. In wire mode, completes the wire instead.</summary>
    public void Select(MapDeviceViewModel device)
    {
        if (WireFrom is { } from)
        {
            CompleteWire(from, device);
            return;
        }

        foreach (var d in Current?.Devices ?? Enumerable.Empty<MapDeviceViewModel>())
        {
            d.IsSelected = ReferenceEquals(d, device);
        }

        foreach (var c in Current?.Connections ?? Enumerable.Empty<MapConnectionViewModel>())
        {
            c.IsSelected = false;
        }

        SelectedConnection = null;
        SelectedDevice = device;
        RefreshEditors();
        RaiseVerdict();
    }

    /// <summary>Selects a wire, surfacing it in the rail. Unrelated wires dim rather than hide.</summary>
    public void Select(MapConnectionViewModel connection)
    {
        foreach (var d in Current?.Devices ?? Enumerable.Empty<MapDeviceViewModel>())
        {
            d.IsSelected = false;
        }

        foreach (var c in Current?.Connections ?? Enumerable.Empty<MapConnectionViewModel>())
        {
            c.IsSelected = ReferenceEquals(c, connection);
        }

        SelectedDevice = null;
        SelectedConnection = connection;
        RefreshEditors();
        RaiseVerdict();
    }

    public void ClearSelection()
    {
        foreach (var d in Current?.Devices ?? Enumerable.Empty<MapDeviceViewModel>())
        {
            d.IsSelected = false;
        }

        foreach (var c in Current?.Connections ?? Enumerable.Empty<MapConnectionViewModel>())
        {
            c.IsSelected = false;
        }

        foreach (var n in Current?.Notes ?? Enumerable.Empty<MapNoteViewModel>())
        {
            n.IsSelected = false;
        }

        SelectedDevice = null;
        SelectedConnection = null;
        SelectedNote = null;
        RefreshEditors();
    }

    private void RefreshEditors()
    {
        DeviceEditor = IsEditing && SelectedDevice is { } device
            ? new MapDeviceEditorViewModel(device.Model, _registry, _types, MapFiles)
            : null;

        ConnectionEditor = IsEditing && SelectedConnection is { } connection
            ? new MapConnectionEditorViewModel(
                connection.Model, _registry, _types, connection.From.Model, connection.To.Model)
            : null;
    }

    /// <summary>
    /// Commits the open editor to the model and rebuilds the map's view models from it.
    /// A rebuild rather than in-place mutation, deliberately: tier and verify are constructor
    /// facts on the probe view models, and half-mutated live state is how maps start lying.
    /// </summary>
    [RelayCommand]
    private void ApplyEditor()
    {
        if (Current is not { } map)
        {
            return;
        }

        string? reselectDevice = null;
        string? reselectConnection = null;

        Checkpoint(DeviceEditor is not null ? "editing a device" : "editing a connection");

        if (DeviceEditor is { } deviceEditor)
        {
            // Which sockets are about to disappear has to be worked out before Apply overwrites
            // the list — afterwards there is nothing left to compare against.
            var dropped = deviceEditor.RemovedPortIds(deviceEditor.Model);
            deviceEditor.Apply();
            reselectDevice = deviceEditor.Model.Id;

            if (dropped.Count > 0)
            {
                // A run anchored to a socket that no longer exists is not deleted — the cable is
                // still there in the building. It just goes back to floating on the edge.
                foreach (var connection in map.Model.Connections)
                {
                    if (connection.FromPort is { } f && dropped.Contains(f))
                    {
                        connection.FromPort = null;
                    }

                    if (connection.ToPort is { } t && dropped.Contains(t))
                    {
                        connection.ToPort = null;
                    }
                }
            }
        }
        else if (ConnectionEditor is { } connectionEditor)
        {
            connectionEditor.Apply();
            reselectConnection = connectionEditor.Model.Id;
        }
        else
        {
            return;
        }

        var rebuilt = RebuildFromModel(map);

        if (reselectDevice is not null)
        {
            var again = rebuilt.Devices.FirstOrDefault(d => d.Id == reselectDevice);
            if (again is not null)
            {
                Select(again);
            }
        }
        else if (reselectConnection is not null)
        {
            var again = rebuilt.Connections.FirstOrDefault(c => c.Model.Id == reselectConnection);
            if (again is not null)
            {
                Select(again);
            }
        }

        Status = "Applied. Save map writes it to disk.";
    }

    /// <summary>Swaps a map's view models for fresh ones built from its (edited) model.</summary>
    private SystemMapViewModel RebuildFromModel(SystemMapViewModel old)
    {
        var rebuilt = new SystemMapViewModel(old.Model, _registry, _types);
        var index = _maps.IndexOf(old);

        if (index >= 0)
        {
            _maps[index] = rebuilt;
            Maps[index] = rebuilt;
        }

        if (ReferenceEquals(Current, old))
        {
            Current = rebuilt;
        }

        // The drill-down history can be holding the view model we just replaced. Left alone, Back
        // would walk you into a map that is no longer the one on disk — a ghost that still draws
        // and still answers questions, with pre-edit answers.
        if (_back.Contains(old))
        {
            var trail = _back.ToArray();
            _back.Clear();

            for (var i = trail.Length - 1; i >= 0; i--)
            {
                _back.Push(ReferenceEquals(trail[i], old) ? rebuilt : trail[i]);
            }

            OnPropertyChanged(nameof(CanGoBack));
        }

        RebuildLegend();
        RollUp();
        return rebuilt;
    }

    /// <summary>Legend tap: isolate one type; tap again to clear. Others drop to ~15%.</summary>
    public void ToggleIsolate(MapLegendRowViewModel row)
    {
        IsolatedType = IsolatedType == row.Type.Id ? null : row.Type.Id;

        foreach (var legendRow in Legend)
        {
            legendRow.IsIsolated = legendRow.Type.Id == IsolatedType;
        }

        ApplyIsolation();
    }

    /// <summary>Re-checks the selected wire (or device) immediately: the rail's "Ping again".</summary>
    [RelayCommand]
    private async Task PingAgainAsync()
    {
        var probe = (MapProbeViewModel?)SelectedConnection ?? SelectedDevice;
        if (probe is null || !probe.HasVerify)
        {
            return;
        }

        try
        {
            await probe.PollAsync(_cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        RollUp();
    }

    /// <summary>
    /// One line for the techdesk: what the map knows is wrong, worst first. Two severities on
    /// purpose. An on-campus verified failure is real ("Cam 3 is not answering"); a reported
    /// node that stopped answering is only silence ("their API went quiet"), which the handoff
    /// files under banner, never alarm.
    /// </summary>
    public (string Text, bool IsFail)? MapAlert()
    {
        var broken = new List<string>();
        var quiet = new List<string>();

        foreach (var map in _maps)
        {
            foreach (var device in map.Devices)
            {
                if (device.CanHoldGate && device.HasVerify && device.IsFailed)
                {
                    broken.Add(device.Label);
                }
                else if (device.IsReported && device.HasVerify && device.Status == VerifyStatus.Failed)
                {
                    quiet.Add(device.Label);
                }
            }

            foreach (var connection in map.Connections.Where(c =>
                         c.IsDown && !c.From.OffCampus && !c.To.OffCampus))
            {
                broken.Add($"{connection.From.Label} \u2192 {connection.To.Label}");
            }
        }

        if (broken.Count > 0)
        {
            var first = broken[0];
            var more = broken.Count - 1;
            return (more == 0
                ? $"MAP \u00b7 {first} is down"
                : $"MAP \u00b7 {first} is down \u00b7 {more} more", true);
        }

        if (quiet.Count > 0)
        {
            var first = quiet[0];
            var more = quiet.Count - 1;
            return (more == 0
                ? $"MAP \u00b7 {first} went quiet \u2014 its API stopped answering"
                : $"MAP \u00b7 {first} went quiet \u00b7 {more} more", false);
        }

        return null;
    }

    /// <summary>
    /// The plain-English conclusion for whatever is selected — the handoff's rule that a trace
    /// showing five red boxes and making the volunteer infer the cause has failed at its job.
    /// Empty when there is nothing useful to conclude.
    /// </summary>
    public string SelectedVerdict
    {
        get
        {
            if (SelectedConnection is { } wire)
            {
                return wire.FlowState switch
                {
                    "down" => $"The signal dies between {wire.From.Label} and {wire.To.Label} \u2014 "
                        + "the link's own check is failing while both ends may be fine. "
                        + "Suspect the cable, the port, or whatever powers the run.",
                    "starved" => wire.From.ShowsFailure && wire.From.HasVerify
                        ? $"Nothing is arriving because {wire.From.Label} itself is down. "
                            + "This run is starved, not broken — fix the box, not the cable."
                        : FindBreak(wire.From) is { } origin
                            ? $"Nothing is arriving here because the break is upstream, at {origin}. "
                                + "This run is starved, not broken."
                            : string.Empty,
                    _ => string.Empty,
                };
            }

            if (SelectedDevice is { } device)
            {
                if (device.IsStarved)
                {
                    return FindBreak(device) is { } origin
                        ? $"This box is fine as far as anyone knows \u2014 nothing is reaching it. "
                            + $"The break is upstream, at {origin}."
                        : string.Empty;
                }

                if (device.ShowsFailure && device.HasVerify)
                {
                    var starving = Current?.Connections
                        .Count(c => ReferenceEquals(c.From, device) && c.FlowState == "starved") ?? 0;

                    return starving > 0
                        ? $"The break is here. {starving} path{(starving == 1 ? string.Empty : "s")} "
                            + "downstream carry nothing because of it \u2014 they are starved, not broken."
                        : "The break is here.";
                }
            }

            return string.Empty;
        }
    }

    public bool HasSelectedVerdict => SelectedVerdict.Length > 0;

    /// <summary>
    /// Walks upstream from a starved element to the first thing that is provably broken: a down
    /// connection, or a verified device whose own check is failing. Breadth-first with a visited
    /// set, so a miswired loop terminates.
    /// </summary>
    private string? FindBreak(MapDeviceViewModel start)
    {
        if (Current is not { } map)
        {
            return null;
        }

        var visited = new HashSet<MapDeviceViewModel> { start };
        var queue = new Queue<MapDeviceViewModel>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var device = queue.Dequeue();

            foreach (var feed in map.Connections.Where(c => ReferenceEquals(c.To, device)))
            {
                if (feed.IsDown)
                {
                    return $"the {feed.Type.Name} link {feed.From.Label} \u2192 {feed.To.Label}";
                }

                if (feed.From.ShowsFailure && feed.From.HasVerify)
                {
                    return feed.From.Label;
                }

                if (visited.Add(feed.From))
                {
                    queue.Enqueue(feed.From);
                }
            }
        }

        return null;
    }

    private void RaiseVerdict()
    {
        OnPropertyChanged(nameof(SelectedVerdict));
        OnPropertyChanged(nameof(HasSelectedVerdict));
    }

    /// <summary>Finds the device whose label best matches a checklist item's wording.</summary>
    public (SystemMapViewModel Map, MapDeviceViewModel Device)? Find(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var needle = text.Trim();

        foreach (var map in _maps)
        {
            foreach (var device in map.Devices)
            {
                if (string.Equals(device.Label, needle, StringComparison.OrdinalIgnoreCase))
                {
                    return (map, device);
                }
            }
        }

        foreach (var map in _maps)
        {
            foreach (var device in map.Devices)
            {
                if (device.Label.Length > 2
                    && (needle.Contains(device.Label, StringComparison.OrdinalIgnoreCase)
                        || device.Label.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                {
                    return (map, device);
                }
            }
        }

        return null;
    }

    private async Task PollAsync()
    {
        if (_polling || _disposed)
        {
            return;
        }

        _polling = true;
        try
        {
            foreach (var probe in _maps.SelectMany(m => m.Probes).Where(p => p.HasVerify).ToList())
            {
                if (_cancellation.IsCancellationRequested)
                {
                    return;
                }

                await probe.PollAsync(_cancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            _polling = false;
            RollUp();
        }
    }

    /// <summary>
    /// Settles derived state everywhere: inferred devices take their upstream's status, linked
    /// maps fold into the device that links to them, and each map's own health lands. Depth-first
    /// with a visiting set, so maps that link in a cycle report Unknown instead of hanging.
    /// </summary>
    public void RollUp()
    {
        foreach (var map in _maps)
        {
            PropagateInferred(map);
        }

        var settled = new Dictionary<string, VerifyStatus>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        VerifyStatus Resolve(SystemMapViewModel map)
        {
            if (settled.TryGetValue(map.FileName, out var done))
            {
                return done;
            }

            if (!visiting.Add(map.FileName))
            {
                return VerifyStatus.Unknown;
            }

            foreach (var device in map.Devices.Where(d => d.HasLink))
            {
                var child = _maps.FirstOrDefault(m =>
                    string.Equals(m.FileName, device.LinksTo, StringComparison.OrdinalIgnoreCase));

                device.LinkedStatus = child is null ? VerifyStatus.Unknown : Resolve(child);
            }

            visiting.Remove(map.FileName);

            var health = map.OwnHealth();
            settled[map.FileName] = health;
            map.Health = health;
            map.RefreshHealthDetail();
            return health;
        }

        foreach (var map in _maps)
        {
            Resolve(map);
        }

        RaiseVerdict();

        // The room list and the stream path are derived from device status, so they have to be
        // recomputed whenever status settles — otherwise the building view keeps showing the
        // faults from the previous poll.
        RefreshProjections();
    }

    /// <summary>
    /// Inferred devices take the worst of what feeds them, walked breadth-first so a chain of
    /// inferred hops (Subsplash → church app) still ends up carrying the on-campus truth.
    /// </summary>
    private static void PropagateInferred(SystemMapViewModel map)
    {
        // A few passes settle any realistic chain; the cap keeps a cycle from spinning.
        for (var pass = 0; pass < 4; pass++)
        {
            foreach (var device in map.Devices.Where(d => d.IsInferred))
            {
                var feeds = map.Connections.Where(c => ReferenceEquals(c.To, device)).ToList();
                if (feeds.Count == 0)
                {
                    device.UpstreamStatus = VerifyStatus.Unknown;
                    continue;
                }

                var worst = VerifyStatus.Passed;
                foreach (var feed in feeds)
                {
                    var upstream = feed.From.EffectiveStatus;
                    if (feed.HasVerify)
                    {
                        upstream = MapProbeViewModel.Worst(upstream, feed.Status);
                    }

                    worst = MapProbeViewModel.Worst(worst, upstream);
                }

                device.UpstreamStatus = worst;
            }
        }
    }

    private void RebuildLegend()
    {
        Legend.Clear();

        if (Current is not { } map)
        {
            return;
        }

        foreach (var type in _types)
        {
            var count = map.Connections.Count(c => ReferenceEquals(c.Type, type)
                || string.Equals(c.Type.Id, type.Id, StringComparison.OrdinalIgnoreCase));

            if (count > 0)
            {
                Legend.Add(new MapLegendRowViewModel(type)
                {
                    Count = count,
                    IsIsolated = type.Id == IsolatedType,
                });
            }
        }

        ApplyIsolation();
    }

    /// <summary>Chip command; toggles off when the active chip is tapped again.</summary>
    [RelayCommand]
    private void SetCategory(string? category)
    {
        CategoryFilter = CategoryFilter == category ? null : category;
        ApplyIsolation();
    }

    private bool PassesCategory(MapConnectionViewModel connection) => CategoryFilter switch
    {
        null => true,
        "faults" => connection.IsDown,
        _ => string.Equals(connection.Type.Category, CategoryFilter, StringComparison.OrdinalIgnoreCase),
    };

    private void ApplyIsolation()
    {
        if (Current is not { } map)
        {
            return;
        }

        foreach (var connection in map.Connections)
        {
            var isolatedOut = IsolatedType is not null
                && !string.Equals(connection.Type.Id, IsolatedType, StringComparison.OrdinalIgnoreCase);

            connection.IsDimmed = isolatedOut || !PassesCategory(connection);
        }

        var filtering = IsolatedType is not null || CategoryFilter is not null;
        foreach (var device in map.Devices)
        {
            device.IsDimmed = filtering
                && !map.Connections.Any(c => !c.IsDimmed
                    && (ReferenceEquals(c.From, device) || ReferenceEquals(c.To, device)));
        }
    }

    // ------------------------------------------------------------------ projections

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsSignalFlow), nameof(IsBuilding), nameof(IsStreamPath),
        nameof(Rooms), nameof(StreamHops), nameof(StreamVerdict), nameof(HasStreamPath))]
    private MapViewMode _viewMode = MapViewMode.SignalFlow;

    public bool IsSignalFlow => ViewMode == MapViewMode.SignalFlow;

    public bool IsBuilding => ViewMode == MapViewMode.Building;

    public bool IsStreamPath => ViewMode == MapViewMode.StreamPath;

    public IReadOnlyList<MapRoomViewModel> Rooms =>
        Current is { } map ? MapProjections.Rooms(map) : Array.Empty<MapRoomViewModel>();

    public IReadOnlyList<MapStreamHopViewModel> StreamHops =>
        Current is { } map ? MapProjections.StreamPath(map) : Array.Empty<MapStreamHopViewModel>();

    public bool HasStreamPath => StreamHops.Count > 0;

    /// <summary>One sentence about the stream path: where it stops, or that it does not.</summary>
    public string StreamVerdict
    {
        get
        {
            var hops = StreamHops;

            if (hops.Count == 0)
            {
                return "No path out of the building yet. Wire something to an encoder or a "
                    + "platform, and mark anything beyond the property line as off campus.";
            }

            if (hops.FirstOrDefault(h => h.IsFirstBreak) is { } broken)
            {
                return broken.HasArriving
                    ? $"Signal stops at {broken.Device.Label} — the {broken.ArrivingLabel} run into "
                        + "it is not arriving. Everything after this is waiting, not broken."
                    : $"{broken.Device.Label} is not passing its own check, so nothing downstream "
                        + "of it has anything to carry.";
            }

            return $"All {hops.Count} hops from {hops[0].Device.Label} to "
                + $"{hops[^1].Device.Label} are clear.";
        }
    }

    /// <summary>The view switcher. Takes the mode name so one handler serves all three buttons.</summary>
    [RelayCommand]
    private void ShowView(string? mode)
    {
        ViewMode = mode switch
        {
            "building" => MapViewMode.Building,
            "stream" => MapViewMode.StreamPath,
            _ => MapViewMode.SignalFlow,
        };

        // The other two projections are read-only by nature: there is nothing on them to drag.
        if (ViewMode != MapViewMode.SignalFlow && IsEditing)
        {
            IsEditing = false;
            WireFrom = null;
            ClearSelection();
            RefreshEditors();
        }
    }

    /// <summary>Recomputes the derived views — after a poll, an edit, or a map switch.</summary>
    private void RefreshProjections()
    {
        OnPropertyChanged(nameof(Rooms));
        OnPropertyChanged(nameof(StreamHops));
        OnPropertyChanged(nameof(HasStreamPath));
        OnPropertyChanged(nameof(StreamVerdict));
    }

    // ------------------------------------------------------------------ undo

    /// <summary>
    /// One point in time: a map's whole model, serialised, and what the operator was about to do.
    /// <para>
    /// Snapshotting the entire map rather than recording inverse operations is the deliberate
    /// choice here. Inverse operations are smaller and faster and get subtly wrong every time
    /// somebody adds a field — deleting a device also deletes its connections, re-fans the ports of
    /// everything it touched, and rewrites the legend, and an "undo delete" that forgets one of
    /// those leaves a map that looks right and is not. A church map is a few dozen boxes; the whole
    /// thing serialises in well under a millisecond, and correctness is worth vastly more here than
    /// the microseconds.
    /// </para>
    /// </summary>
    private readonly record struct UndoStep(string FileName, string Json, string Label);

    private readonly Stack<UndoStep> _undo = new();

    /// <summary>Enough to cover a bad afternoon, bounded so a long session cannot grow forever.</summary>
    private const int UndoDepth = 60;

    public bool CanUndo => _undo.Count > 0;

    /// <summary>What Ctrl+Z would take back, for the button's tooltip.</summary>
    public string UndoLabel => _undo.Count > 0 ? $"Undo {_undo.Peek().Label}" : "Nothing to undo";

    /// <summary>
    /// Records the current state before a change. Every mutating path calls this <em>first</em>;
    /// the label is what the operator did, phrased so it reads after the word "Undo".
    /// </summary>
    private void Checkpoint(string label)
    {
        if (Current is not { } map)
        {
            return;
        }

        try
        {
            _undo.Push(new UndoStep(
                map.FileName,
                JsonSerializer.Serialize(map.Model, ChecklistWriter.WriteOptions),
                label));
        }
        catch (Exception)
        {
            // A map that will not serialise is a map that could not have been saved either. Losing
            // undo is the smaller problem, and blocking the edit over it would be the larger one.
            return;
        }

        while (_undo.Count > UndoDepth)
        {
            var kept = _undo.ToArray().Take(UndoDepth).Reverse().ToList();
            _undo.Clear();

            foreach (var step in kept)
            {
                _undo.Push(step);
            }
        }

        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(UndoLabel));
    }

    /// <summary>Throws away the history — after a load, or a switch to a different map.</summary>
    private void ForgetUndo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _undo.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(UndoLabel));
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undo.Count == 0 || Current is not { } map)
        {
            return;
        }

        var step = _undo.Pop();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(UndoLabel));

        if (step.FileName != map.FileName)
        {
            // The history belongs to a map that is no longer open. Undoing into it behind the
            // operator's back would be worse than refusing.
            Status = "That change was on a different map. Open it to undo there.";
            return;
        }

        SystemMap restored;

        try
        {
            restored = JsonSerializer.Deserialize<SystemMap>(step.Json, ChecklistLoader.JsonOptions)
                ?? throw new InvalidOperationException("empty snapshot");
        }
        catch (Exception ex)
        {
            Status = $"Could not undo: {ex.Message}";
            return;
        }

        restored.SourceFile = map.Model.SourceFile;

        ClearSelection();
        var index = _maps.IndexOf(map);
        var rebuilt = new SystemMapViewModel(restored, _registry, _types);

        if (index >= 0)
        {
            _maps[index] = rebuilt;
            Maps[index] = rebuilt;
        }

        Current = rebuilt;
        RebuildLegend();
        RollUp();
        RefreshEditors();
        Status = $"Undid {step.Label}. Save map to write it to disk.";
    }

    // ------------------------------------------------------------------ editing

    [RelayCommand]
    private void ToggleEditing()
    {
        IsEditing = !IsEditing;
        WireFrom = null;

        if (!IsEditing)
        {
            ClearSelection();
        }

        RefreshEditors();
    }

    public MapDeviceViewModel? AddDevice(double x, double y)
    {
        if (Current is not { } map)
        {
            return null;
        }

        Checkpoint("adding a device");

        var model = new MapDevice
        {
            Id = SystemMapStore.NewId("device"),
            Label = "New device",
            Tier = MapTiers.Inferred,
            X = Math.Max(0, Math.Round(x)),
            Y = Math.Max(0, Math.Round(y)),
        };

        map.Model.Devices.Add(model);
        var vm = new MapDeviceViewModel(model, _registry, null);
        map.Devices.Add(vm);
        map.RefreshExtent();
        Select(vm);
        return vm;
    }

    /// <summary>The note under the editor's caret, if any. Selecting one clears the others.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoteAttachmentLabel))]
    private MapNoteViewModel? _selectedNote;

    public MapNoteViewModel? AddNote(double x, double y)
    {
        if (Current is not { } map)
        {
            return null;
        }

        Checkpoint("adding a note");

        // A note about the selected device attaches to it, because that is almost always what
        // somebody means when they select a box and then reach for the note button.
        var about = SelectedDevice;

        var model = new MapNote
        {
            Id = SystemMapStore.NewId("note"),
            Text = string.Empty,
            X = Math.Max(0, Math.Round(x)),
            Y = Math.Max(0, Math.Round(y)),
            AboutDevice = about?.Id,
        };

        map.Model.Notes.Add(model);
        var vm = new MapNoteViewModel(model, about);
        map.Notes.Add(vm);
        map.RefreshExtent();
        SelectNote(vm);
        Status = about is null
            ? "Note added. Type into it, and drag it where it belongs."
            : $"Note added, attached to {about.Label}. It will follow the box.";
        return vm;
    }

    /// <summary>What the rail says about where a note is pinned.</summary>
    public string NoteAttachmentLabel => SelectedNote?.About is { } device
        ? $"Attached to {device.Label}. It follows the box when you move it."
        : "Free-standing. Drag it anywhere on the canvas.";

    [RelayCommand]
    private void ToggleNoteTone()
    {
        if (SelectedNote is not { } note)
        {
            return;
        }

        Checkpoint("changing a note");
        note.Model.Tone = note.IsWarning ? MapNoteTones.Plain : MapNoteTones.Warning;

        // The tone lives on the model, so the view model has to be told the derived flag moved.
        note.OnToneChanged();
    }

    [RelayCommand]
    private void DetachNote()
    {
        if (SelectedNote is not { About: not null } note)
        {
            return;
        }

        Checkpoint("detaching a note");
        note.Model.AboutDevice = null;

        // Rebuilding is how the note loses its live tether without the view model having to
        // support un-wiring an event it subscribed to in its constructor.
        if (Current is { } map)
        {
            RebuildFromModel(map);
        }
    }

    public void SelectNote(MapNoteViewModel note)
    {
        ClearSelection();

        foreach (var other in Current?.Notes ?? Enumerable.Empty<MapNoteViewModel>())
        {
            other.IsSelected = ReferenceEquals(other, note);
        }

        SelectedNote = note;
    }

    [RelayCommand]
    private void BeginWire()
    {
        if (SelectedDevice is { } from)
        {
            WireFrom = from;
        }
    }

    /// <summary>Starts a wire from a specific device — the drag gesture's entry point.</summary>
    public void BeginWireFrom(MapDeviceViewModel device)
    {
        WireFrom = device;
        _wirePort = null;
        _wireMode = WireModes.From;
    }

    /// <summary>How the armed wire relates to the socket it started at.</summary>
    public static class WireModes
    {
        /// <summary>The armed end is the source: signal leaves here.</summary>
        public const string From = "from";

        /// <summary>The armed end is the destination: we are wiring backwards into it.</summary>
        public const string To = "to";

        /// <summary>One cable, both directions.</summary>
        public const string Both = "both";
    }

    private string? _wirePort;
    private string _wireMode = WireModes.From;

    /// <summary>
    /// Arms a run at a specific socket.
    /// <para>
    /// The direction usually needs no asking: an output can only send and an input can only
    /// receive, so the port's own side answers it. Only a socket that genuinely carries both ways —
    /// an AES50 jack, an ethernet port — has to put the question to a human, and the menu on the
    /// canvas offers exactly the choices that socket allows and no others.
    /// </para>
    /// </summary>
    public void BeginPortWire(MapDeviceViewModel device, string portId, string mode)
    {
        WireFrom = device;
        _wirePort = portId;
        _wireMode = mode;

        var port = device.Model.Ports.FirstOrDefault(p => p.Id == portId);
        var name = port?.Label ?? "that socket";

        Status = mode switch
        {
            WireModes.To => $"Wiring into {name} — now click what feeds it.",
            WireModes.Both => $"Wiring {name} both ways — now click the socket at the other end.",
            _ => $"Wiring from {name} — now click where it lands.",
        };
    }

    /// <summary>Completes an armed run at a socket, or anywhere on a box when none is named.</summary>
    public void FinishWireAtPort(MapDeviceViewModel device, string? portId)
    {
        if (WireFrom is not { } armed)
        {
            return;
        }

        if (ReferenceEquals(armed, device))
        {
            Status = "A run has to go somewhere else.";
            return;
        }

        var backwards = _wireMode == WireModes.To;

        CompleteWire(
            backwards ? device : armed,
            backwards ? armed : device,
            backwards ? portId : _wirePort,
            backwards ? _wirePort : portId,
            _wireMode == WireModes.Both);
    }

    /// <summary>
    /// Finishes a drag-wire onto whatever is under the pointer. Public because the drag lives in
    /// the view: only it knows what the pointer is over.
    /// </summary>
    public void FinishWire(MapDeviceViewModel? target)
    {
        if (WireFrom is not null && target is not null)
        {
            FinishWireAtPort(target, null);
            return;
        }

        CancelWire();
    }

    /// <summary>The device whose box contains a canvas point, if any.</summary>
    public MapDeviceViewModel? DeviceAt(Avalonia.Point point) =>
        Current?.Devices.FirstOrDefault(d =>
            point.X >= d.X && point.X <= d.X + MapDeviceViewModel.BoxWidth
            && point.Y >= d.Y && point.Y <= d.Y + MapDeviceViewModel.BoxHeight);

    [RelayCommand]
    private void CancelWire()
    {
        WireFrom = null;
        _wirePort = null;
        _wireMode = WireModes.From;
    }

    private void CompleteWire(
        MapDeviceViewModel from,
        MapDeviceViewModel to,
        string? fromPort = null,
        string? toPort = null,
        bool bidirectional = false)
    {
        WireFrom = null;
        var armedPort = _wirePort;
        _wirePort = null;
        _wireMode = WireModes.From;

        if (Current is not { } map || ReferenceEquals(from, to))
        {
            return;
        }

        // Same pair, same sockets is a duplicate. Same pair on *different* sockets is not — a desk
        // fed from two outputs of one box is ordinary, and refusing it would be wrong.
        if (map.Connections.Any(c => ReferenceEquals(c.From, from) && ReferenceEquals(c.To, to)
                 && c.Model.FromPort == fromPort && c.Model.ToPort == toPort))
        {
            Status = fromPort is null && toPort is null
                ? $"{from.Label} already feeds {to.Label}."
                : $"That run from {from.Label} to {to.Label} is already on the map.";
            return;
        }

        Checkpoint("drawing a connection");

        // Defaults to the last type used on this map — the handoff's rule for the drop picker.
        var lastType = map.Connections.LastOrDefault()?.Type ?? _types.FirstOrDefault()
            ?? MapConnectionTypes.Unknown;

        var model = new MapConnection
        {
            Id = SystemMapStore.NewId($"{from.Id}-{to.Id}"),
            From = from.Id,
            To = to.Id,
            Type = lastType.Id,
            FromPort = Named(from, fromPort),
            ToPort = Named(to, toPort),
            Bidirectional = bidirectional,
        };
        model.FlowSeed = SystemMapStore.StableHash(model.Id);

        map.Model.Connections.Add(model);

        var vm = new MapConnectionViewModel(model, from, to, lastType, _registry);
        map.Connections.Add(vm);
        map.RefreshPorts();
        RebuildLegend();
        Select(vm);

        var route = vm.HasPortRoute ? $" · {vm.PortRoute}" : string.Empty;
        var arrow = bidirectional ? "↔" : "→";
        Status = $"Wired {from.Label} {arrow} {to.Label} as {lastType.Name}{route}. "
            + "Change the type in the rail.";

        _ = armedPort;
    }

    /// <summary>A socket id, but only if that device really has it.</summary>
    private static string? Named(MapDeviceViewModel device, string? portId) =>
        portId is { Length: > 0 } id && device.Model.Ports.Any(p => p.Id == id) ? id : null;

    [RelayCommand]
    private void DeleteSelected()
    {
        if (Current is not { } map)
        {
            return;
        }

        if (SelectedNote is { } note)
        {
            Checkpoint("deleting a note");
            map.Model.Notes.Remove(note.Model);
            map.Notes.Remove(note);
            SelectedNote = null;
            return;
        }

        if (SelectedConnection is { } connection)
        {
            Checkpoint($"deleting {connection.Title}");
            map.Model.Connections.Remove(connection.Model);
            map.Connections.Remove(connection);
            SelectedConnection = null;
            map.RefreshPorts();
            RebuildLegend();
            return;
        }

        if (SelectedDevice is { } device)
        {
            Checkpoint($"deleting {device.Label}");

            foreach (var dangling in map.Connections
                         .Where(c => ReferenceEquals(c.From, device) || ReferenceEquals(c.To, device))
                         .ToList())
            {
                map.Model.Connections.Remove(dangling.Model);
                map.Connections.Remove(dangling);
            }

            foreach (var orphan in map.Notes.Where(n => n.About is not null
                         && ReferenceEquals(n.About, device)).ToList())
            {
                // Detached, not deleted. Somebody wrote those words for a reason, and losing them
                // silently because a box was removed is how a map stops being trusted.
                orphan.Model.AboutDevice = null;
            }

            map.Model.Devices.Remove(device.Model);
            map.Devices.Remove(device);
            SelectedDevice = null;
            map.RefreshExtent();
            map.RefreshPorts();
            RebuildLegend();
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (Current is not { } map)
        {
            return;
        }

        foreach (var device in map.Devices)
        {
            device.Apply();
        }

        try
        {
            var file = string.IsNullOrWhiteSpace(map.FileName)
                ? SystemMapStore.FileNameFor(map.Name)
                : map.FileName;

            _store.Save(map.Model, file);
            map.Model.SourceFile = file;
            Status = $"Saved to {_store.PathFor(file)}";
        }
        catch (Exception ex)
        {
            Status = $"Could not save: {ex.Message}";
        }
    }

    /// <summary>The empty state's Create button.</summary>
    [RelayCommand]
    private void CreateFirstMap()
    {
        var name = string.IsNullOrWhiteSpace(NewMapName) ? "System map" : NewMapName.Trim();
        var created = CreateMap(name);
        if (created is not null)
        {
            IsEditing = true;
            NewMapName = string.Empty;
        }
    }

    /// <summary>
    /// The empty state's other button: a worked example of a real church rig.
    /// <para>
    /// Modelled on an actual building, because a three-box toy does not show the thing that
    /// makes maps worth having — a console with ten connections, wireless that starts at a
    /// receiver rather than in mid-air, and a digital snake carrying both directions at once.
    /// Every label is meant to be renamed; the shape is the lesson, not the inventory.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void CreateExampleMap()
    {
        // Five columns of signal flow. Places live on each device's Location, so the same data
        // will drive the floorplan view when it lands.
        const double srcX = 16, boxX = 430, deskX = 844, distX = 1258, outX = 1672;

        static MapDevice Dev(
            string id, string label, string kind, double x, double y, string place,
            string? detail = null, string? dominant = null, bool hub = false,
            VerifySpec? verify = null) => new()
        {
            Id = id, Label = label, Kind = kind, X = x, Y = y,
            Location = place, Detail = detail, DominantType = dominant, Hub = hub, Verify = verify,
            Tier = verify is null ? MapTiers.Inferred : MapTiers.Verified,
        };

        // Sockets are attached after the fact so the device list stays a readable table.
        static MapDevice Port(MapDevice device, params (string Id, string Label, string Side, string? Detail)[] ports)
        {
            foreach (var (id, label, side, detail) in ports)
            {
                device.Ports.Add(new MapPort { Id = id, Label = label, Side = side, Detail = detail });
            }

            return device;
        }

        static MapConnection Wire(string from, string to, string type, string? label = null) => new()
        {
            Id = $"{from}--{to}--{type}",
            From = from, To = to, Type = type, Label = label,
            FlowSeed = SystemMapStore.StableHash($"{from}{to}{type}"),
        };

        var model = new SystemMap
        {
            Name = "Main system",
            Summary = "A worked example of a real church rig. Rename these to your gear, or delete "
                + "them and start clean. Every box says which room it lives in.",
            Columns =
            {
                new MapColumn { Label = "STAGE & PLATFORM", X = srcX },
                new MapColumn { Label = "STAGE BOX & RECEIVERS", X = boxX },
                new MapColumn { Label = "SOUND BOOTH", X = deskX },
                new MapColumn { Label = "DISTRIBUTION", X = distX },
                new MapColumn { Label = "OUTPUTS", X = outX },
            },
            Devices =
            {
                // ---- wired sources, by place ----
                Dev("piano-mic", "Piano Mic", MapDeviceKinds.Audio, srcX, 60, "Piano pit", "XLR", "xlr"),
                Dev("cong-left", "Left Congregation Mic", MapDeviceKinds.Audio, srcX, 140, "Piano pit", "XLR", "xlr"),
                Dev("gtr-elec", "Electric Guitar", MapDeviceKinds.Audio, srcX, 220, "Stage Left", "DI", "xlr"),
                Dev("gtr-acoustic", "Acoustic Guitar", MapDeviceKinds.Audio, srcX, 300, "Stage", "DI", "xlr"),
                Dev("violin", "Violin", MapDeviceKinds.Audio, srcX, 380, "Stage", "DI", "xlr"),
                Dev("gtr-bass", "Bass Guitar", MapDeviceKinds.Audio, srcX, 460, "Stage Right", "DI", "xlr"),
                Dev("drums", "Electric Drums", MapDeviceKinds.Audio, srcX, 540, "Drum pit", "STEREO PAIR", "xlr"),
                Dev("cong-right", "Right Congregation Mic", MapDeviceKinds.Audio, srcX, 620, "Drum pit", "XLR", "xlr"),

                // ---- wireless sources ----
                Dev("mic-white", "White Mic", MapDeviceKinds.Audio, srcX, 730, "Stage", "WIRELESS HANDHELD", "wl-audio"),
                Dev("mic-yellow", "Yellow Mic", MapDeviceKinds.Audio, srcX, 810, "Stage", "WIRELESS HANDHELD", "wl-audio"),
                Dev("mic-green", "Green Mic", MapDeviceKinds.Audio, srcX, 890, "Stage", "WIRELESS HANDHELD", "wl-audio"),
                Dev("mic-purple", "Purple Mic", MapDeviceKinds.Audio, srcX, 970, "Stage", "WIRELESS HANDHELD", "wl-audio"),
                Dev("mic-lapel", "Lapel", MapDeviceKinds.Audio, srcX, 1050, "Stage", "WIRELESS BELT PACK", "wl-audio"),
                Dev("mic-baptismal", "Baptismal Mic", MapDeviceKinds.Audio, srcX, 1130, "Baptismal", "WIRELESS", "wl-audio"),

                // ---- cameras, by place ----
                Dev("cam-1", "NDI Camera 1", MapDeviceKinds.Camera, srcX, 1240, "Sound booth", "NDI · GIVE IT ITS ADDRESS", "ndi"),
                Dev("cam-2", "NDI Camera 2", MapDeviceKinds.Camera, srcX, 1320, "Right Sanctuary", "NDI · GIVE IT ITS ADDRESS", "ndi"),
                Dev("cam-3", "NDI Camera 3", MapDeviceKinds.Camera, srcX, 1400, "Left Sanctuary", "NDI · GIVE IT ITS ADDRESS", "ndi"),

                // ---- stage box and receivers ----
                Port(
                    Dev("s16", "S16 stage box", MapDeviceKinds.Audio, boxX, 300, "Piano pit",
                        "16 IN · AES50 TO X32", "aes50", hub: true),
                    ("s16-aes", "AES50 A", MapPortSides.Both, "TO THE X32"),
                    ("s16-iem", "IEM SENDS", MapPortSides.Out, "OUT 1-6")),
                Dev("receivers", "Mic Receivers", MapDeviceKinds.Audio, boxX, 890, "Sound booth",
                    "6 CHANNELS · RACK", "wl-audio", hub: true),
                Dev("focusrite", "Focusrite interface", MapDeviceKinds.Audio, boxX, 1130, "Sound booth",
                    "PROPRESENTER AUDIO OUT", "xlr"),

                // ---- the booth ----
                Port(
                    Dev("x32", "X32", MapDeviceKinds.Audio, deskX, 480, "Sound booth",
                        "32 IN · THE HUB OF EVERYTHING", "aes50", hub: true),
                    ("x32-aes", "AES50 A", MapPortSides.Both, "THE SNAKE"),
                    ("x32-ch1", "CH 1-16", MapPortSides.In, "FROM THE RECEIVERS"),
                    ("x32-ch25", "CH 25-26", MapPortSides.In, "FROM PROPRESENTER"),
                    ("x32-main", "MAIN L/R", MapPortSides.Out, "THE HOUSE"),
                    ("x32-sub", "SUB AUX", MapPortSides.Out, null),
                    ("x32-net", "ETHERNET", MapPortSides.Both, "TO THE M4250"),
                    ("x32-snake", "OUT 9-16", MapPortSides.Out, "ANALOG SNAKE TO MEDIA")),
                Dev("propresenter", "ProPresenter", MapDeviceKinds.Computer, deskX, 1050, "Sound booth",
                    "SLIDES & LYRICS", "cat6", hub: true),

                // ---- distribution ----
                Dev("m4250", "M4250 switch", MapDeviceKinds.Network, distX, 900, "Sound booth",
                    "AV NETWORK BACKBONE", "cat6", hub: true),
                Dev("x32-compact", "X32 Compact", MapDeviceKinds.Audio, distX, 480, "Media room",
                    "LIVESTREAM MIX · FED BY SNAKE", "analog-snake", hub: true),
                Dev("iem-tx", "IEM transmitters", MapDeviceKinds.Audio, distX, 180, "Piano pit",
                    "FED FROM THE S16", "wl-audio"),

                // ---- outputs ----
                Dev("speakers", "Main Speakers", MapDeviceKinds.Display, outX, 380, "Stage", "L / R", "xlr"),
                Dev("subs", "Subs", MapDeviceKinds.Display, outX, 460, "Stage", "AUX FED", "xlr"),
                Dev("iems", "In Ear Monitors", MapDeviceKinds.Audio, outX, 180, "Piano pit",
                    "PERSONAL MIXES", "wl-audio"),
                Dev("livestream", "Livestream station", MapDeviceKinds.Computer, outX, 900, "Media room",
                    "ENCODER & SWITCHER", "ndi", hub: true),
            },
        };

        // ---- wired instruments and mics into the S16 ----
        foreach (var id in new[]
                 {
                     "piano-mic", "cong-left", "gtr-elec", "gtr-acoustic",
                     "violin", "gtr-bass", "drums", "cong-right",
                 })
        {
            model.Connections.Add(Wire(id, "s16", "xlr"));
        }

        // ---- wireless mics land on a receiver, never in mid-air ----
        foreach (var id in new[]
                 {
                     "mic-white", "mic-yellow", "mic-green", "mic-purple", "mic-lapel", "mic-baptismal",
                 })
        {
            model.Connections.Add(Wire(id, "receivers", "wl-audio"));
        }

        var fromReceivers = Wire("receivers", "x32", "xlr", "RECEIVER OUTS");
        fromReceivers.ToPort = "x32-ch1";
        model.Connections.Add(fromReceivers);

        model.Connections.Add(Wire("propresenter", "focusrite", "cat6", "USB AUDIO"));

        var fromFocusrite = Wire("focusrite", "x32", "xlr", "XLR INS");
        fromFocusrite.ToPort = "x32-ch25";
        model.Connections.Add(fromFocusrite);

        // ---- the digital snake: one cable, inputs up and IEM sends back down it ----
        var snake = Wire("s16", "x32", "aes50", "16 INPUTS UP · IEM SENDS BACK");
        snake.Bidirectional = true;
        snake.FromPort = "s16-aes";
        snake.ToPort = "x32-aes";
        model.Connections.Add(snake);
        var iemFeed = Wire("s16", "iem-tx", "xlr", "IEM FEEDS");
        iemFeed.FromPort = "s16-iem";
        model.Connections.Add(iemFeed);
        model.Connections.Add(Wire("iem-tx", "iems", "wl-audio"));

        // ---- house outputs ----
        var mains = Wire("x32", "speakers", "xlr", "MAIN L/R");
        mains.FromPort = "x32-main";
        model.Connections.Add(mains);

        var subs = Wire("x32", "subs", "xlr", "SUB AUX");
        subs.FromPort = "x32-sub";
        model.Connections.Add(subs);

        // ---- network: one trunk, talking both ways ----
        var trunk = Wire("x32", "m4250", "cat6", "CONTROL & AoIP");
        trunk.Bidirectional = true;
        trunk.FromPort = "x32-net";
        model.Connections.Add(trunk);
        model.Connections.Add(Wire("propresenter", "m4250", "cat6"));

        // ---- the stream path ----
        var toMedia = Wire("x32", "x32-compact", "analog-snake", "SNAKE TO MEDIA ROOM");
        toMedia.FromPort = "x32-snake";
        model.Connections.Add(toMedia);
        model.Connections.Add(Wire("x32-compact", "livestream", "xlr", "STREAM MIX"));
        model.Connections.Add(Wire("cam-1", "m4250", "ndi"));
        model.Connections.Add(Wire("cam-2", "m4250", "ndi"));
        model.Connections.Add(Wire("cam-3", "m4250", "ndi"));
        model.Connections.Add(Wire("m4250", "livestream", "ndi", "NDI TO ENCODER"));

        // Notes: the two things a map made of boxes and lines cannot say.
        model.Notes.Add(new MapNote
        {
            Id = "note-snake",
            AboutDevice = "x32",
            Tone = MapNoteTones.Warning,
            Text = "One cable carries the stage inputs up and the IEM mixes back down. "
                + "Unplug it and the band loses their ears at the same moment you lose the inputs.",
            X = deskX - 46,
            Y = 660,
        });

        model.Notes.Add(new MapNote
        {
            Id = "note-example",
            Text = "This is an example, not your building. Rename each box to your own gear, "
                + "delete what you do not have, and give the ones that matter a check.",
            X = srcX,
            Y = 1500,
        });

        try
        {
            _store.Save(model, SystemMapStore.DefaultFileName);
        }
        catch (Exception ex)
        {
            Status = $"Could not create the example: {ex.Message}";
            return;
        }

        Load();
        IsEditing = true;
        Status = "Example rig created. Rename the boxes to your gear, and give each one a check.";
    }

    public SystemMapViewModel? CreateMap(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var file = SystemMapStore.FileNameFor(name);
        if (_store.Exists(file))
        {
            Status = $"{file} already exists.";
            return null;
        }

        var model = new SystemMap { Name = name.Trim(), SourceFile = file };

        try
        {
            _store.Save(model, file);
        }
        catch (Exception ex)
        {
            Status = $"Could not create {file}: {ex.Message}";
            return null;
        }

        var vm = new SystemMapViewModel(model, _registry, _types);
        _maps.Add(vm);
        Maps.Add(vm);
        MapFiles.Add(file);
        Open(vm);
        return vm;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer?.Stop();
        _timer = null;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
