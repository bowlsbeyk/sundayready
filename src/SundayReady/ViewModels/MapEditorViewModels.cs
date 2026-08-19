using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SundayReady.Models;
using SundayReady.Services;

namespace SundayReady.ViewModels;

/// <summary>
/// The verifier half of both map editors, shared because it is the same idea in both places:
/// pick a kind, fill in the fields that kind cares about.
/// <para>
/// Deliberately the same <see cref="VerifySpec"/> the checklists use — the map's checks and the
/// checklist's verifiers are one mechanism, which is the handoff's own rule ("don't build a
/// second one"). The kind-specific field visibility mirrors the checklist editor.
/// </para>
/// </summary>
public abstract partial class MapVerifyEditorViewModel : ObservableObject
{
    public const string NoVerify = "(none)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUrl), nameof(ShowContains), nameof(ShowHost), nameof(ShowPort),
        nameof(ShowProcessName), nameof(ShowNameContains), nameof(ShowPath), nameof(HasVerify),
        nameof(GuideText))]
    private string _verifyKind = NoVerify;

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private string _contains = string.Empty;

    [ObservableProperty]
    private string _host = string.Empty;

    [ObservableProperty]
    private string _port = string.Empty;

    [ObservableProperty]
    private string _processName = string.Empty;

    [ObservableProperty]
    private string _nameContains = string.Empty;

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private int _maxAttempts = VerifySpec.DefaultMaxAttempts;

    protected MapVerifyEditorViewModel(VerifySpec? verify, VerifierRegistry registry)
    {
        VerifyKinds.Add(NoVerify);
        foreach (var kind in registry.Kinds.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            VerifyKinds.Add(kind);
        }

        if (verify is null)
        {
            return;
        }

        _verifyKind = VerifyKinds.Contains(verify.Kind) ? verify.Kind : verify.Kind;
        if (!VerifyKinds.Contains(_verifyKind))
        {
            // An unknown kind in the file still round-trips rather than being silently dropped.
            VerifyKinds.Add(_verifyKind);
        }

        _url = verify.Url ?? string.Empty;
        _contains = verify.Contains ?? string.Empty;
        _host = verify.Host ?? string.Empty;
        _port = verify.Port?.ToString() ?? string.Empty;
        _processName = verify.ProcessName ?? string.Empty;
        _nameContains = verify.NameContains ?? string.Empty;
        _path = verify.Path ?? string.Empty;
        _maxAttempts = verify.MaxAttempts;
    }

    public ObservableCollection<string> VerifyKinds { get; } = new();

    public bool HasVerify => VerifyKind != NoVerify && !string.IsNullOrWhiteSpace(VerifyKind);

    public bool ShowUrl => VerifyKind == "httpContains";

    public bool ShowContains => VerifyKind == "httpContains";

    public bool ShowHost => VerifyKind is "hostReachable" or "internetReachable";

    public bool ShowPort => VerifyKind == "hostReachable";

    public bool ShowProcessName => VerifyKind == "processRunning";

    public bool ShowNameContains => VerifyKind is "ndiSourcePresent" or "audioDevicePresent";

    public bool ShowPath => VerifyKind == "fileExists";

    /// <summary>The same inline hint the checklist editor shows, from the same source.</summary>
    public string GuideText => Guides.For(VerifyKind)?.Headline ?? string.Empty;

    public bool HasGuide => GuideText.Length > 0;

    /// <summary>The spec these fields describe, or null when no check is wanted.</summary>
    protected VerifySpec? BuildVerify()
    {
        if (!HasVerify)
        {
            return null;
        }

        return new VerifySpec
        {
            Kind = VerifyKind,
            Url = Blank(Url),
            Contains = string.IsNullOrEmpty(Contains) ? null : Contains,
            Host = Blank(Host),
            Port = int.TryParse(Port, out var port) && port > 0 ? port : null,
            ProcessName = Blank(ProcessName),
            NameContains = Blank(NameContains),
            Path = Blank(Path),
            MaxAttempts = MaxAttempts < 1 ? VerifySpec.DefaultMaxAttempts : MaxAttempts,
        };
    }

    protected static string? Blank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>One socket in the component editor's live preview, positioned as the map will.</summary>
public sealed record MapPreviewPort(string ShortLabel, string FullLabel, double Top, bool RightSide, bool IsBanked)
{
    public double TileSize => IsBanked ? 5 : 18;

    public double TileFontSize => ShortLabel.Length >= 4 ? 6.5 : 8.5;

    /// <summary>Canvas.Left inside a 262-wide preview box: straddling the edge, like the map.</summary>
    public double Left => RightSide ? 262 - (TileSize / 2) : -(TileSize / 2);
}

/// <summary>
/// Everything about one device the editor can change. Fields here, applied to the model in one
/// go — the probe view models are deliberately immutable about tier and verify, so an apply
/// rebuilds the map from its model rather than mutating live state halfway.
/// </summary>
public sealed partial class MapDeviceEditorViewModel : MapVerifyEditorViewModel
{
    public const string NoLink = "(no sub-map)";

    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private string _detail;

    [ObservableProperty]
    private string _kind;

    [ObservableProperty]
    private bool _hub;

    [ObservableProperty]
    private string _tier;

    [ObservableProperty]
    private bool _offCampus;

    [ObservableProperty]
    private string _location;

    [ObservableProperty]
    private string _dominantType;

    [ObservableProperty]
    private string _linksTo;

    [ObservableProperty]
    private string _checkSteps;

    public MapDeviceEditorViewModel(
        MapDevice model,
        VerifierRegistry registry,
        IEnumerable<MapConnectionType> types,
        IEnumerable<string> mapFiles,
        IEnumerable<DeviceTemplate>? templates = null)
        : base(model.Verify, registry)
    {
        Model = model;
        _templates = (templates ?? DeviceTemplates.BuiltIn).ToList();

        foreach (var kind in MapDeviceKinds.All)
        {
            Kinds.Add(kind);
        }

        Tiers.Add(MapTiers.Verified);
        Tiers.Add(MapTiers.Reported);
        Tiers.Add(MapTiers.Inferred);
        Tiers.Add(MapTiers.Human);

        DominantTypes.Add("(none)");
        foreach (var type in types)
        {
            DominantTypes.Add(type.Id);
        }

        LinkTargets.Add(NoLink);
        foreach (var file in mapFiles.Where(f => !string.Equals(f, model.Id, StringComparison.Ordinal)))
        {
            LinkTargets.Add(file);
        }

        _label = model.Label;
        _detail = model.Detail ?? string.Empty;
        _kind = model.Kind;
        _hub = model.Hub;
        _tier = model.Tier ?? (model.Verify is null ? MapTiers.Inferred : MapTiers.Verified);
        _offCampus = model.OffCampus;
        _location = model.Location ?? string.Empty;
        _dominantType = model.DominantType ?? "(none)";
        _linksTo = model.LinksTo ?? NoLink;
        _checkSteps = string.Join(Environment.NewLine, model.CheckSteps);

        foreach (var port in model.Ports)
        {
            Ports.Add(new MapPortEditorViewModel(port));
        }

        Templates.Add(NoTemplate);
        foreach (var template in _templates)
        {
            Templates.Add(template.Name);
        }
    }

    private readonly List<DeviceTemplate> _templates;

    /// <summary>
    /// The component alone, rendered as the map will render it — the DipTrace idea: you are not
    /// editing rows in a form, you are shaping the thing you will later click on. Tiles sit at
    /// the true pitch on the true edges with the true compressed labels, so what you see here is
    /// exactly what lands on the canvas at Apply.
    /// </summary>
    public IReadOnlyList<MapPreviewPort> PreviewPorts
    {
        get
        {
            var named = Ports.Where(p => !string.IsNullOrWhiteSpace(p.Label)).ToList();
            var lefts = named.Where(p => p.Side == MapPortSides.In).ToList();
            var rights = named.Where(p => MapPortSides.AcceptsOut(p.Side)).ToList();
            var result = new List<MapPreviewPort>();

            void Edge(List<MapPortEditorViewModel> edge, bool rightSide)
            {
                var banked = edge.Count > MapDeviceViewModel.BankThreshold;
                var pitch = banked ? MapDeviceViewModel.BankPitch : MapDeviceViewModel.PortPitch;

                for (var i = 0; i < edge.Count; i++)
                {
                    result.Add(new MapPreviewPort(
                        MapPortAnchor.Shorten(edge[i].Label),
                        edge[i].Label,
                        MapDeviceViewModel.PortFirstTop + (i * pitch) + (banked ? 3 : 8) - 11,
                        rightSide,
                        banked));
                }
            }

            Edge(lefts, false);
            Edge(rights, true);
            return result;
        }
    }

    /// <summary>The preview box's height — the same maths the map uses.</summary>
    public double PreviewHeight
    {
        get
        {
            var named = Ports.Count(p => !string.IsNullOrWhiteSpace(p.Label));
            var lefts = Ports.Count(p => !string.IsNullOrWhiteSpace(p.Label) && p.Side is MapPortSides.In or MapPortSides.Both);
            var rights = Ports.Count(p => !string.IsNullOrWhiteSpace(p.Label) && MapPortSides.AcceptsOut(p.Side));
            var rows = Math.Max(lefts, rights);

            if (rows == 0)
            {
                return MapDeviceViewModel.BoxHeight;
            }

            var listRows = named > 16 ? 0 : Math.Min(named, 6);

            return Math.Max(
                Math.Max(MapDeviceViewModel.BoxHeight,
                    MapDeviceViewModel.PortFirstTop + MapDeviceViewModel.EdgeSpan(rows) + 22),
                52 + (listRows * 15) + 24);
        }
    }

    /// <summary>The preview's meta strip. Everything is FREE here: nothing is wired yet.</summary>
    public string PreviewSummary
    {
        get
        {
            var count = Ports.Count(p => !string.IsNullOrWhiteSpace(p.Label));
            return count == 0 ? string.Empty : $"{count} PORTS";
        }
    }

    /// <summary>Tells the view the rendering changed. Called by the window on any edit.</summary>
    public void RefreshPreview()
    {
        OnPropertyChanged(nameof(PreviewPorts));
        OnPropertyChanged(nameof(PreviewHeight));
        OnPropertyChanged(nameof(PreviewSummary));
    }

    /// <summary>The library pane's rows — every template the picker knows, by name.</summary>
    public IReadOnlyList<string> Library => _templates.Select(t => t.Name).ToList();

    /// <summary>
    /// Loads a library entry wholesale: the port list is REPLACED, kind, accent and hub follow.
    /// This is the pick-a-component gesture, distinct from the additive "Add its ports" — and it
    /// is safe to be decisive here because nothing touches the map until Apply, and Close
    /// without applying walks away from all of it.
    /// </summary>
    public void LoadFromLibrary(string? name)
    {
        var template = _templates.FirstOrDefault(t => t.Name == name);

        if (template is null)
        {
            return;
        }

        Ports.Clear();

        foreach (var port in template.Ports)
        {
            Ports.Add(new MapPortEditorViewModel(new MapPort
            {
                Id = SystemMapStore.NewId("port"),
                Label = port.Label,
                Side = port.Side,
                Detail = port.Detail,
                Type = port.Type,
            }));
        }

        Kind = template.Kind;

        if (template.DominantType is { } dominant && DominantTypes.Contains(dominant))
        {
            DominantType = dominant;
        }

        Hub = template.Hub;

        if (string.IsNullOrWhiteSpace(Label) || Label == "New device")
        {
            Label = template.Name;
        }

        TemplateReport = $"LOADED {template.Name.ToUpperInvariant()} · {template.Ports.Count} PORTS · REPLACED THE LIST";
        OnPropertyChanged(nameof(HasPorts));
        RefreshPreview();
    }

    /// <summary>
    /// Adds a just-saved template to this live editor's picker without rebuilding the editor —
    /// a rebuild would re-read the model and eat any port rows not yet applied.
    /// </summary>
    public void OfferTemplate(DeviceTemplate template)
    {
        _templates.Add(template);
        Templates.Add(template.Name);
    }

    /// <summary>
    /// This editor's state as a shareable template — the community exporter's raw material.
    /// The name comes from the device's label because that is what the operator called the
    /// thing; the id is fresh so two people templating the same desk never collide.
    /// </summary>
    public DeviceTemplate ToTemplate() => new()
    {
        Id = SystemMapStore.NewId(Label),
        Name = string.IsNullOrWhiteSpace(Label) ? "Untitled device" : Label.Trim(),
        Kind = Kind,
        DominantType = DominantType == "(none)" ? null : DominantType,
        Hub = Hub,
        Ports = Ports
            .Where(p => !string.IsNullOrWhiteSpace(p.Label))
            .Select(p => new DeviceTemplatePort(
                p.Label.Trim(), p.Side, BlankDetail(p.Detail), p.Type))
            .ToList(),
    };

    private static string? BlankDetail(string detail) =>
        string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();

    public const string NoTemplate = "(pick a template)";

    [ObservableProperty]
    private string _template = NoTemplate;

    [ObservableProperty]
    private string _templateReport = string.Empty;

    public ObservableCollection<string> Templates { get; } = new();

    /// <summary>
    /// Adds a template's sockets to the port list — the handoff's "editable, never typed from
    /// scratch". Additive and idempotent: ports whose label is already on the list are skipped,
    /// so applying twice, or applying on top of hand-typed rows, never duplicates or destroys.
    /// Kind, accent and hub only fill in when they are still at their defaults — the template
    /// must never overwrite something the operator already chose.
    /// </summary>
    [RelayCommand]
    private void ApplyTemplate()
    {
        var template = _templates.FirstOrDefault(t => t.Name == Template);

        if (template is null)
        {
            return;
        }

        var existing = new HashSet<string>(
            Ports.Select(p => p.Label.Trim()), StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var port in template.Ports)
        {
            if (existing.Contains(port.Label))
            {
                continue;
            }

            Ports.Add(new MapPortEditorViewModel(new MapPort
            {
                Id = SystemMapStore.NewId("port"),
                Label = port.Label,
                Side = port.Side,
                Detail = port.Detail,
                Type = port.Type,
            }));
            added++;
        }

        if (Kind == MapDeviceKinds.Device)
        {
            Kind = template.Kind;
        }

        if (DominantType == "(none)" && template.DominantType is { } dominant
            && DominantTypes.Contains(dominant))
        {
            DominantType = dominant;
        }

        if (template.Hub)
        {
            Hub = true;
        }

        TemplateReport = added == 0
            ? "EVERY PORT IT DEFINES IS ALREADY ON THE LIST"
            : $"{added} PORTS ADDED · EDIT OR REMOVE ANY OF THEM";
        OnPropertyChanged(nameof(HasPorts));
    }

    public MapDevice Model { get; }

    public ObservableCollection<string> Kinds { get; } = new();

    public ObservableCollection<string> Tiers { get; } = new();

    public ObservableCollection<string> DominantTypes { get; } = new();

    public ObservableCollection<string> LinkTargets { get; } = new();

    /// <summary>Writes every field back onto the model. The workspace rebuilds after this.</summary>
    /// <summary>
    /// The device's sockets, in the order they will sit down its edge. Optional throughout — a map
    /// is useful the moment two boxes are joined, and demanding a port list first would make the
    /// first five minutes miserable.
    /// </summary>
    public ObservableCollection<MapPortEditorViewModel> Ports { get; } = new();

    public bool HasPorts => Ports.Count > 0;

    [RelayCommand]
    private void AddPort()
    {
        Ports.Add(new MapPortEditorViewModel(null)
        {
            Label = $"Port {Ports.Count + 1}",
        });

        OnPropertyChanged(nameof(HasPorts));
    }

    [RelayCommand]
    private void RemovePort(MapPortEditorViewModel? port)
    {
        if (port is not null && Ports.Remove(port))
        {
            OnPropertyChanged(nameof(HasPorts));
        }
    }

    /// <summary>Order is meaningful — it is the order sockets sit down the box's edge.</summary>
    [RelayCommand]
    private void MovePortUp(MapPortEditorViewModel? port)
    {
        var index = port is null ? -1 : Ports.IndexOf(port);

        if (index > 0)
        {
            Ports.Move(index, index - 1);
        }
    }

    [RelayCommand]
    private void MovePortDown(MapPortEditorViewModel? port)
    {
        var index = port is null ? -1 : Ports.IndexOf(port);

        if (index >= 0 && index < Ports.Count - 1)
        {
            Ports.Move(index, index + 1);
        }
    }

    public void Apply()
    {
        Model.Label = string.IsNullOrWhiteSpace(Label) ? Model.Label : Label.Trim();
        Model.Detail = Blank(Detail);
        Model.Kind = Kind;
        Model.Hub = Hub;
        Model.OffCampus = OffCampus;
        Model.Location = Blank(Location);
        Model.DominantType = DominantType == "(none)" ? null : DominantType;
        Model.LinksTo = LinksTo == NoLink ? null : LinksTo;
        Model.Verify = BuildVerify();
        Model.CheckSteps = CheckSteps
            .Split('\n')
            .Select(l => l.Trim().TrimEnd('\r'))
            .Where(l => l.Length > 0)
            .ToList();

        // A port with no name is a row somebody started and abandoned. Keeping it would put an
        // unlabelled tick on the box, which is worse than no tick at all.
        Model.Ports = Ports
            .Where(p => !string.IsNullOrWhiteSpace(p.Label))
            .Select(p => p.ToModel())
            .ToList();

        // Tier follows the same honesty rule as loading: claiming "verified" with no check to
        // back it would let a guess wear the one tier that can hold the gate.
        Model.Tier = Tier == MapTiers.Verified && Model.Verify is null
            ? MapTiers.Inferred
            : Tier;
    }

    /// <summary>
    /// Ports the operator deleted here, so the map can drop the runs still pointing at them rather
    /// than leaving connections anchored to sockets that no longer exist.
    /// </summary>
    public IReadOnlyList<string> RemovedPortIds(MapDevice before) => before.Ports
        .Select(p => p.Id)
        .Where(id => Ports.All(p => p.Id != id || string.IsNullOrWhiteSpace(p.Label)))
        .ToList();
}

/// <summary>Everything about one connection the editor can change.</summary>
/// <summary>
/// One row in a device's port list.
/// <para>
/// The id is the load-bearing field and the one nobody types. Connections point at ports by id, so
/// renaming <c>OUT 1</c> to <c>MAIN L/R</c> has to keep every run that lands there — which it does,
/// because the row carries the original id forward. A port added here gets a fresh one.
/// </para>
/// </summary>
public sealed partial class MapPortEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private string _side;

    [ObservableProperty]
    private string _detail;

    public MapPortEditorViewModel(MapPort? model)
    {
        Id = model?.Id ?? SystemMapStore.NewId("port");
        _label = model?.Label ?? string.Empty;
        _side = model?.Side ?? MapPortSides.In;
        _detail = model?.Detail ?? string.Empty;
        Type = model?.Type;
    }

    public string Id { get; }

    /// <summary>Carried through untouched — the rail has no picker for it yet.</summary>
    public string? Type { get; }

    public IReadOnlyList<string> Sides { get; } = MapPortSides.All;

    public MapPort ToModel() => new()
    {
        Id = Id,
        Label = Label.Trim(),
        Side = Sides.Contains(Side) ? Side : MapPortSides.In,
        Detail = string.IsNullOrWhiteSpace(Detail) ? null : Detail.Trim(),
        Type = Type,
    };
}

public sealed partial class MapConnectionEditorViewModel : MapVerifyEditorViewModel
{
    [ObservableProperty]
    private string _type;

    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private string _lengthFt;

    [ObservableProperty]
    private bool _standby;

    [ObservableProperty]
    private bool _bidirectional;

    [ObservableProperty]
    private string _fromPort;

    [ObservableProperty]
    private string _toPort;

    /// <summary>The empty choice in both port pickers — "anywhere along the edge".</summary>
    public const string NoPort = "(no specific port)";

    public MapConnectionEditorViewModel(
        MapConnection model,
        VerifierRegistry registry,
        IEnumerable<MapConnectionType> types,
        MapDevice? from = null,
        MapDevice? to = null)
        : base(model.Verify, registry)
    {
        Model = model;

        foreach (var type in types)
        {
            TypeIds.Add(type.Id);
        }

        _type = model.Type ?? TypeIds.FirstOrDefault() ?? "cat6";
        if (!TypeIds.Contains(_type))
        {
            TypeIds.Add(_type);
        }

        _label = model.Label ?? string.Empty;
        _lengthFt = model.LengthFt?.ToString() ?? string.Empty;
        _standby = model.Standby;
        _bidirectional = model.Bidirectional;

        // Only sockets that can take this end of the run are offered. A two-way run needs a socket
        // that carries both ways at each end, which is exactly the rule that stops somebody
        // labelling a snake as arriving on an output.
        FromPorts.Add(NoPort);
        foreach (var port in from?.Ports ?? Enumerable.Empty<MapPort>())
        {
            if (model.Bidirectional ? port.Side == MapPortSides.Both : MapPortSides.AcceptsOut(port.Side))
            {
                FromPorts.Add(port.Label);
                _fromById[port.Label] = port.Id;
            }
        }

        ToPorts.Add(NoPort);
        foreach (var port in to?.Ports ?? Enumerable.Empty<MapPort>())
        {
            if (model.Bidirectional ? port.Side == MapPortSides.Both : MapPortSides.AcceptsIn(port.Side))
            {
                ToPorts.Add(port.Label);
                _toById[port.Label] = port.Id;
            }
        }

        _fromPort = LabelFor(from, model.FromPort, FromPorts);
        _toPort = LabelFor(to, model.ToPort, ToPorts);
    }

    private readonly Dictionary<string, string> _fromById = new();
    private readonly Dictionary<string, string> _toById = new();

    public MapConnection Model { get; }

    public ObservableCollection<string> TypeIds { get; } = new();

    public ObservableCollection<string> FromPorts { get; } = new();

    public ObservableCollection<string> ToPorts { get; } = new();

    /// <summary>Hidden entirely when neither end declares a socket this run could use.</summary>
    public bool HasPortChoices => FromPorts.Count > 1 || ToPorts.Count > 1;

    /// <summary>
    /// The stored id back to the label shown in the picker. A run pointing at a port that has since
    /// been deleted falls back to "no specific port" rather than showing a stale name.
    /// </summary>
    private static string LabelFor(MapDevice? device, string? portId, ICollection<string> offered)
    {
        if (device is null || string.IsNullOrEmpty(portId))
        {
            return NoPort;
        }

        var match = device.Ports.FirstOrDefault(p => p.Id == portId);
        return match is not null && offered.Contains(match.Label) ? match.Label : NoPort;
    }

    public void Apply()
    {
        Model.Type = Type;
        Model.Label = Blank(Label);
        Model.LengthFt = int.TryParse(LengthFt, out var ft) && ft > 0 ? ft : null;
        Model.Standby = Standby;
        Model.Bidirectional = Bidirectional;
        Model.FromPort = _fromById.TryGetValue(FromPort ?? string.Empty, out var f) ? f : null;
        Model.ToPort = _toById.TryGetValue(ToPort ?? string.Empty, out var t) ? t : null;
        Model.Verify = BuildVerify();
    }
}
