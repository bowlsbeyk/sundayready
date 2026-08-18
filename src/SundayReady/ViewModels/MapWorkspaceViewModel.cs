using System.Collections.ObjectModel;
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
    }

    public SystemMap Model { get; }

    public string FileName => Model.SourceFile;

    public string Name => Model.Name;

    public string? Summary => Model.Summary;

    public bool HasSummary => !string.IsNullOrWhiteSpace(Model.Summary);

    public ObservableCollection<MapDeviceViewModel> Devices { get; } = new();

    public ObservableCollection<MapConnectionViewModel> Connections { get; } = new();

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

    public double CanvasWidth => Devices.Count == 0
        ? 1500
        : Math.Max(1500, Devices.Max(d => d.X) + MapDeviceViewModel.BoxWidth + 120);

    public double CanvasHeight => Devices.Count == 0
        ? 900
        : Math.Max(900, Devices.Max(d => d.Y) + MapDeviceViewModel.BoxHeight + 120);

    public void RefreshExtent()
    {
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        OnPropertyChanged(nameof(ColumnBands));
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

    public bool HasCurrent => Current is not null;

    public bool IsEmpty => Current is null;

    public bool CanGoBack => _back.Count > 0;

    public bool HasSelection => SelectedDevice is not null || SelectedConnection is not null;

    public bool HasSelectedConnection => SelectedConnection is not null;

    public bool IsWiring => WireFrom is not null;

    public string Title => Current?.Name ?? "System map";

    public string MapsFolder => _store.Directory;

    public string ModeLabel => IsWiring
        ? "Click the device this one feeds into"
        : IsEditing ? "Editing — drag to move, select a device then Wire to connect" : string.Empty;

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

        SelectedDevice = null;
        SelectedConnection = null;
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
    }

    public MapDeviceViewModel? AddDevice(double x, double y)
    {
        if (Current is not { } map)
        {
            return null;
        }

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

    [RelayCommand]
    private void BeginWire()
    {
        if (SelectedDevice is { } from)
        {
            WireFrom = from;
        }
    }

    [RelayCommand]
    private void CancelWire() => WireFrom = null;

    private void CompleteWire(MapDeviceViewModel from, MapDeviceViewModel to)
    {
        WireFrom = null;

        if (Current is not { } map || ReferenceEquals(from, to))
        {
            return;
        }

        if (map.Connections.Any(c => ReferenceEquals(c.From, from) && ReferenceEquals(c.To, to)))
        {
            Status = $"{from.Label} already feeds {to.Label}.";
            return;
        }

        // Defaults to the last type used on this map — the handoff's rule for the drop picker.
        var lastType = map.Connections.LastOrDefault()?.Type ?? _types.FirstOrDefault()
            ?? MapConnectionTypes.Unknown;

        var model = new MapConnection
        {
            Id = SystemMapStore.NewId($"{from.Id}-{to.Id}"),
            From = from.Id,
            To = to.Id,
            Type = lastType.Id,
        };
        model.FlowSeed = SystemMapStore.StableHash(model.Id);

        map.Model.Connections.Add(model);

        var vm = new MapConnectionViewModel(model, from, to, lastType, _registry);
        map.Connections.Add(vm);
        RebuildLegend();
        Select(vm);
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (Current is not { } map)
        {
            return;
        }

        if (SelectedConnection is { } connection)
        {
            map.Model.Connections.Remove(connection.Model);
            map.Connections.Remove(connection);
            SelectedConnection = null;
            RebuildLegend();
            return;
        }

        if (SelectedDevice is { } device)
        {
            foreach (var dangling in map.Connections
                         .Where(c => ReferenceEquals(c.From, device) || ReferenceEquals(c.To, device))
                         .ToList())
            {
                map.Model.Connections.Remove(dangling.Model);
                map.Connections.Remove(dangling);
            }

            map.Model.Devices.Remove(device.Model);
            map.Devices.Remove(device);
            SelectedDevice = null;
            map.RefreshExtent();
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
