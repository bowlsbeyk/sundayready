using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SundayReady.Models;
using SundayReady.Services;

namespace SundayReady.ViewModels;

/// <summary>One loaded map: its boxes, its lines, and how it is doing as a whole.</summary>
public sealed partial class SystemMapViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HealthLabel), nameof(IsHealthy), nameof(IsBroken))]
    private VerifyStatus _health = VerifyStatus.Unknown;

    public SystemMapViewModel(SystemMap model, VerifierRegistry registry)
    {
        Model = model;

        var byId = new Dictionary<string, MapComponentViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in model.Components)
        {
            var vm = new MapComponentViewModel(component, registry);
            Components.Add(vm);
            byId[vm.Id] = vm;
        }

        foreach (var connection in model.Connections)
        {
            // A connection naming a component that is not on the map is dropped rather than
            // drawn to nowhere. Hand-edited files get this wrong, and half a line is worse
            // than a missing one.
            if (byId.TryGetValue(connection.From, out var from) && byId.TryGetValue(connection.To, out var to))
            {
                Connections.Add(new MapConnectionViewModel(connection, from, to, registry));
            }
        }
    }

    public SystemMap Model { get; }

    public string FileName => Model.SourceFile;

    public string Name => Model.Name;

    public string? Summary => Model.Summary;

    public bool HasSummary => !string.IsNullOrWhiteSpace(Model.Summary);

    public ObservableCollection<MapComponentViewModel> Components { get; } = new();

    public ObservableCollection<MapConnectionViewModel> Connections { get; } = new();

    public IEnumerable<MapProbeViewModel> Probes =>
        Components.Cast<MapProbeViewModel>().Concat(Connections);

    public bool IsHealthy => Health == VerifyStatus.Passed;

    public bool IsBroken => Health is VerifyStatus.Failed or VerifyStatus.Unsupported;

    /// <summary>Named so the techdesk can print it without knowing what a VerifyStatus is.</summary>
    public string HealthLabel => Health switch
    {
        VerifyStatus.Passed => "All checks passing",
        VerifyStatus.Polling => "Checking…",
        VerifyStatus.Failed => $"{Failing.Count()} not passing",
        VerifyStatus.Unsupported => "A check on this map is not recognised",
        _ => "Nothing on this map is checked",
    };

    /// <summary>Everything currently failing, for the rail and for "what is wrong".</summary>
    public IEnumerable<MapProbeViewModel> Failing => Probes.Where(p => p.IsFailed);

    /// <summary>Canvas extent, so the scroll area is the size of the drawing plus room to grow.</summary>
    public double CanvasWidth => Components.Count == 0
        ? 1200
        : Math.Max(1200, Components.Max(c => c.X) + MapComponentViewModel.Width + 320);

    public double CanvasHeight => Components.Count == 0
        ? 800
        : Math.Max(800, Components.Max(c => c.Y) + MapComponentViewModel.Height + 260);

    public void RefreshExtent()
    {
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
    }

    /// <summary>
    /// This map's own health, ignoring anything it links to — <see cref="MapWorkspaceViewModel"/>
    /// folds the linked maps in, because only it knows what the other maps are doing.
    /// </summary>
    public VerifyStatus OwnHealth()
    {
        var checkedProbes = Probes.Where(p => p.HasVerify || p.Status == VerifyStatus.Unsupported).ToList();
        var linked = Components.Where(c => c.LinkedStatus != VerifyStatus.Unknown).ToList();

        if (checkedProbes.Count == 0 && linked.Count == 0)
        {
            return VerifyStatus.Unknown;
        }

        var worst = VerifyStatus.Passed;
        foreach (var probe in checkedProbes)
        {
            worst = MapProbeViewModel.Worst(worst, probe.Status);
        }

        foreach (var component in linked)
        {
            worst = MapProbeViewModel.Worst(worst, component.LinkedStatus);
        }

        return worst;
    }
}

/// <summary>
/// Every map the church has, and the one currently on screen.
/// <para>
/// All maps are polled, not just the visible one. That is the whole point of nesting: a box on the
/// top-level map goes red because something three levels down did, and it can only do that if the
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

    private DispatcherTimer? _timer;
    private bool _polling;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrent), nameof(CanGoBack), nameof(Title), nameof(IsEmpty))]
    private SystemMapViewModel? _current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private MapComponentViewModel? _selectedComponent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private MapConnectionViewModel? _selectedConnection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeLabel))]
    private bool _isEditing;

    /// <summary>The box a wire is being dragged from, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWiring), nameof(ModeLabel))]
    private MapComponentViewModel? _wireFrom;

    [ObservableProperty]
    private string _status = string.Empty;

    public MapWorkspaceViewModel(SystemMapStore store, VerifierRegistry registry)
    {
        _store = store;
        _registry = registry;
        Load();
    }

    public ObservableCollection<SystemMapViewModel> Maps { get; } = new();

    /// <summary>Map file names, for the "links to" picker in the inspector.</summary>
    public ObservableCollection<string> MapFiles { get; } = new();

    public ObservableCollection<string> ComponentKinds { get; } =
        new(MapComponentKinds.All);

    public bool HasCurrent => Current is not null;

    public bool IsEmpty => Current is null;

    public bool CanGoBack => _back.Count > 0;

    public bool HasSelection => SelectedComponent is not null || SelectedConnection is not null;

    public bool IsWiring => WireFrom is not null;

    public string Title => Current?.Name ?? "System map";

    public string MapsFolder => _store.Directory;

    public string ModeLabel => IsWiring
        ? "Click the box this one feeds into"
        : IsEditing ? "Editing — drag to move, click a box then Wire to connect" : string.Empty;

    /// <summary>Starts polling. Separate from the constructor so a view can bind first.</summary>
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

    /// <summary>Re-reads every map from disk, keeping the current one open if it still exists.</summary>
    public void Load()
    {
        var openFile = Current?.FileName;

        _maps.Clear();
        Maps.Clear();
        MapFiles.Clear();
        _back.Clear();

        var errors = new List<string>();

        foreach (var file in _store.ListFiles())
        {
            try
            {
                var map = new SystemMapViewModel(_store.Load(file), _registry);
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
        SelectedComponent = null;
        SelectedConnection = null;

        Status = errors.Count > 0
            ? string.Join(Environment.NewLine, errors)
            : _maps.Count == 0
                ? $"No maps yet. They live in {_store.Directory}."
                : string.Empty;

        OnPropertyChanged(nameof(CanGoBack));
        RollUp();
    }

    /// <summary>Opens a map, remembering where we came from so Back works.</summary>
    public void Open(SystemMapViewModel map, bool remember = true)
    {
        if (Current is { } current && remember && !ReferenceEquals(current, map))
        {
            _back.Push(current);
        }

        Current = map;
        SelectedComponent = null;
        SelectedConnection = null;
        OnPropertyChanged(nameof(CanGoBack));
    }

    [RelayCommand]
    private void Back()
    {
        if (_back.Count == 0)
        {
            return;
        }

        Current = _back.Pop();
        SelectedComponent = null;
        SelectedConnection = null;
        OnPropertyChanged(nameof(CanGoBack));
    }

    /// <summary>Follows a box's link, if it has one. Returns false when there is nothing to open.</summary>
    public bool Drill(MapComponentViewModel component)
    {
        if (!component.HasLink)
        {
            return false;
        }

        var target = _maps.FirstOrDefault(m =>
            string.Equals(m.FileName, component.LinksTo, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            Status = $"“{component.Label}” links to {component.LinksTo}, which is not in {_store.Directory}.";
            return false;
        }

        Open(target);
        return true;
    }

    /// <summary>Selects a box and shows it in the inspector. In wire mode, completes the wire.</summary>
    public void Select(MapComponentViewModel component)
    {
        if (WireFrom is { } from)
        {
            CompleteWire(from, component);
            return;
        }

        foreach (var c in Current?.Components ?? Enumerable.Empty<MapComponentViewModel>())
        {
            c.IsSelected = ReferenceEquals(c, component);
        }

        foreach (var c in Current?.Connections ?? Enumerable.Empty<MapConnectionViewModel>())
        {
            c.IsSelected = false;
        }

        SelectedConnection = null;
        SelectedComponent = component;
    }

    public void Select(MapConnectionViewModel connection)
    {
        foreach (var c in Current?.Components ?? Enumerable.Empty<MapComponentViewModel>())
        {
            c.IsSelected = false;
        }

        foreach (var c in Current?.Connections ?? Enumerable.Empty<MapConnectionViewModel>())
        {
            c.IsSelected = ReferenceEquals(c, connection);
        }

        SelectedComponent = null;
        SelectedConnection = connection;
    }

    /// <summary>Finds the box whose label best matches a checklist item, for "show me on the map".</summary>
    public (SystemMapViewModel Map, MapComponentViewModel Component)? Find(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var needle = text.Trim();

        foreach (var map in _maps)
        {
            foreach (var component in map.Components)
            {
                if (string.Equals(component.Label, needle, StringComparison.OrdinalIgnoreCase))
                {
                    return (map, component);
                }
            }
        }

        // Nothing exact, so try a containment match either way round — a checklist item reading
        // "Cam 3 present in vMix inputs" should still find the box called "Cam 3".
        foreach (var map in _maps)
        {
            foreach (var component in map.Components)
            {
                if (component.Label.Length > 2
                    && (needle.Contains(component.Label, StringComparison.OrdinalIgnoreCase)
                        || component.Label.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                {
                    return (map, component);
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
            // Every map, not just the visible one — see the class comment.
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
    /// Settles every map's health, folding a linked map's result into the box that links to it.
    /// <para>
    /// Depth-first with a visiting set, so a map that links to itself — directly or round a longer
    /// loop — reports Unknown for that link instead of hanging the app.
    /// </para>
    /// </summary>
    public void RollUp()
    {
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
                // A cycle. Treat this arm as unknown rather than recursing forever.
                return VerifyStatus.Unknown;
            }

            foreach (var component in map.Components.Where(c => c.HasLink))
            {
                var child = _maps.FirstOrDefault(m =>
                    string.Equals(m.FileName, component.LinksTo, StringComparison.OrdinalIgnoreCase));

                component.LinkedStatus = child is null ? VerifyStatus.Unknown : Resolve(child);
            }

            visiting.Remove(map.FileName);

            var health = map.OwnHealth();
            settled[map.FileName] = health;
            map.Health = health;
            return health;
        }

        foreach (var map in _maps)
        {
            Resolve(map);
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
            SelectedComponent = null;
            SelectedConnection = null;
        }
    }

    /// <summary>Drops a new box on the canvas. The caller decides where.</summary>
    public MapComponentViewModel? AddComponent(double x, double y)
    {
        if (Current is not { } map)
        {
            return null;
        }

        var model = new MapComponent
        {
            Id = SystemMapStore.NewId("node"),
            Label = "New component",
            Kind = MapComponentKinds.Device,
            X = Math.Max(0, Math.Round(x)),
            Y = Math.Max(0, Math.Round(y)),
        };

        map.Model.Components.Add(model);
        var vm = new MapComponentViewModel(model, _registry);
        map.Components.Add(vm);
        map.RefreshExtent();
        Select(vm);
        return vm;
    }

    [RelayCommand]
    private void BeginWire()
    {
        if (SelectedComponent is { } from)
        {
            WireFrom = from;
        }
    }

    [RelayCommand]
    private void CancelWire() => WireFrom = null;

    private void CompleteWire(MapComponentViewModel from, MapComponentViewModel to)
    {
        WireFrom = null;

        if (Current is not { } map || ReferenceEquals(from, to))
        {
            return;
        }

        var already = map.Connections.Any(c =>
            ReferenceEquals(c.From, from) && ReferenceEquals(c.To, to));

        if (already)
        {
            Status = $"{from.Label} already feeds {to.Label}.";
            return;
        }

        var model = new MapConnection { From = from.Id, To = to.Id };
        map.Model.Connections.Add(model);

        var vm = new MapConnectionViewModel(model, from, to, _registry);
        map.Connections.Add(vm);
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
            return;
        }

        if (SelectedComponent is { } component)
        {
            // Lines to and from it go too, or the file would keep references to a box that is gone.
            foreach (var dangling in map.Connections
                         .Where(c => ReferenceEquals(c.From, component) || ReferenceEquals(c.To, component))
                         .ToList())
            {
                map.Model.Connections.Remove(dangling.Model);
                map.Connections.Remove(dangling);
            }

            map.Model.Components.Remove(component.Model);
            map.Components.Remove(component);
            SelectedComponent = null;
            map.RefreshExtent();
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (Current is not { } map)
        {
            return;
        }

        foreach (var component in map.Components)
        {
            component.Apply();
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

    /// <summary>Creates a new empty map and opens it.</summary>
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

        var vm = new SystemMapViewModel(model, _registry);
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
