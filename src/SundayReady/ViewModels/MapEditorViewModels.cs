using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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
        IEnumerable<string> mapFiles)
        : base(model.Verify, registry)
    {
        Model = model;

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
    }

    public MapDevice Model { get; }

    public ObservableCollection<string> Kinds { get; } = new();

    public ObservableCollection<string> Tiers { get; } = new();

    public ObservableCollection<string> DominantTypes { get; } = new();

    public ObservableCollection<string> LinkTargets { get; } = new();

    /// <summary>Writes every field back onto the model. The workspace rebuilds after this.</summary>
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

        // Tier follows the same honesty rule as loading: claiming "verified" with no check to
        // back it would let a guess wear the one tier that can hold the gate.
        Model.Tier = Tier == MapTiers.Verified && Model.Verify is null
            ? MapTiers.Inferred
            : Tier;
    }
}

/// <summary>Everything about one connection the editor can change.</summary>
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

    public MapConnectionEditorViewModel(
        MapConnection model,
        VerifierRegistry registry,
        IEnumerable<MapConnectionType> types)
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
    }

    public MapConnection Model { get; }

    public ObservableCollection<string> TypeIds { get; } = new();

    public void Apply()
    {
        Model.Type = Type;
        Model.Label = Blank(Label);
        Model.LengthFt = int.TryParse(LengthFt, out var ft) && ft > 0 ? ft : null;
        Model.Standby = Standby;
        Model.Verify = BuildVerify();
    }
}
