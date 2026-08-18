using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SundayReady.Models;
using SundayReady.Services;

namespace SundayReady.ViewModels;

/// <summary>
/// Anything on a map that can be checked — a device or a connection.
/// <para>
/// Deliberately thinner than a checklist item. An item has to tick itself, log the transition and
/// hold an override; a map probe only has to say what it can see right now. Sharing the retry
/// budget and the status vocabulary keeps the two layers reading the same way without dragging the
/// checklist's bookkeeping into a diagram.
/// </para>
/// </summary>
public abstract partial class MapProbeViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOk), nameof(IsFailed), nameof(IsPolling), nameof(StatusLabel))]
    private VerifyStatus _status = VerifyStatus.Unknown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private int _attempts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult), nameof(StatusLabel))]
    private string? _lastResult;

    protected MapProbeViewModel(VerifySpec? verify, IReadOnlyList<string> checkSteps, VerifierRegistry registry)
    {
        Verify = verify;
        CheckSteps = checkSteps;

        if (verify is not null)
        {
            Verifier = registry.TryGet(verify, out var found) ? found : null;
            Status = Verifier is null ? VerifyStatus.Unsupported : VerifyStatus.Polling;
        }
    }

    public VerifySpec? Verify { get; }

    public IVerifier? Verifier { get; }

    public IReadOnlyList<string> CheckSteps { get; }

    public bool HasVerify => Verify is not null && Verifier is not null;

    public bool HasCheckSteps => CheckSteps.Count > 0;

    public bool HasResult => !string.IsNullOrWhiteSpace(LastResult);

    public bool IsOk => Status == VerifyStatus.Passed;

    public bool IsFailed => Status is VerifyStatus.Failed or VerifyStatus.Unsupported;

    public bool IsPolling => Status == VerifyStatus.Polling;

    public int MaxAttempts => Verify?.MaxAttempts ?? VerifySpec.DefaultMaxAttempts;

    /// <summary>What the check is doing, in the verifier's own words.</summary>
    public string StatusLabel => Status switch
    {
        VerifyStatus.Passed => Verifier is null ? "ok" : Verifier.Describe(Verify!),
        VerifyStatus.Polling => $"checking · {Attempts} of {MaxAttempts}",
        VerifyStatus.Failed => LastResult ?? "not answering",
        VerifyStatus.Unsupported => $"unknown check: {Verify?.Kind}",
        _ => string.Empty,
    };

    /// <summary>
    /// One attempt. Never throws — a verifier that cannot answer returns a failing outcome, and a
    /// map that stopped polling because one box was unreachable would be worse than useless.
    /// </summary>
    public async Task PollAsync(CancellationToken cancellationToken)
    {
        if (Verify is null || Verifier is null || Status == VerifyStatus.Unsupported)
        {
            return;
        }

        VerifyOutcome outcome;
        try
        {
            outcome = await Verifier.CheckAsync(Verify, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            outcome = VerifyOutcome.Fail(ex.Message, TimeSpan.Zero);
        }

        LastResult = outcome.Result;

        if (outcome.Passed)
        {
            Attempts = 0;
            Status = VerifyStatus.Passed;
            return;
        }

        Attempts++;
        Status = Attempts >= MaxAttempts ? VerifyStatus.Failed : VerifyStatus.Polling;
    }

    /// <summary>
    /// Status is declared here, so the generated change hook belongs to this class and a subclass
    /// cannot implement it. Subclasses override <see cref="StatusChanged"/> instead.
    /// </summary>
    partial void OnStatusChanged(VerifyStatus value) => StatusChanged();

    protected virtual void StatusChanged()
    {
    }

    /// <summary>Failure beats polling beats ok beats unknown.</summary>
    public static VerifyStatus Worst(VerifyStatus a, VerifyStatus b)
    {
        static int Rank(VerifyStatus s) => s switch
        {
            VerifyStatus.Failed => 4,
            VerifyStatus.Unsupported => 3,
            VerifyStatus.Polling => 2,
            VerifyStatus.Passed => 1,
            _ => 0,
        };

        return Rank(a) >= Rank(b) ? a : b;
    }
}

/// <summary>A device on the map, with the handoff's tier semantics baked in.</summary>
public sealed partial class MapDeviceViewModel : MapProbeViewModel
{
    /// <summary>Node box, per the handoff's 2a geometry. Fixed so wire geometry needs no layout pass.</summary>
    public const double BoxWidth = 210;

    public const double BoxHeight = 64;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Centre), nameof(RightPort), nameof(LeftPort))]
    private double _x;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Centre), nameof(RightPort), nameof(LeftPort))]
    private double _y;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Set true by the legend's isolate filter when this device carries none of the type.</summary>
    [ObservableProperty]
    private bool _isDimmed;

    /// <summary>Health of the map this device links to, folded into <see cref="EffectiveStatus"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveStatus), nameof(ShowsFailure), nameof(ShowsOk), nameof(ShowsHollowDot))]
    private VerifyStatus _linkedStatus = VerifyStatus.Unknown;

    /// <summary>For inferred devices: the upstream's status, set by the workspace rollup.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveStatus), nameof(ShowsFailure), nameof(ShowsOk), nameof(ShowsHollowDot))]
    private VerifyStatus _upstreamStatus = VerifyStatus.Unknown;

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string _kind = MapDeviceKinds.Device;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    private string? _detail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLink))]
    private string? _linksTo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLocation))]
    private string? _location;

    public MapDeviceViewModel(MapDevice model, VerifierRegistry registry, MapConnectionType? dominantType)
        : base(model.Verify, model.CheckSteps, registry)
    {
        Model = model;
        Id = model.Id;
        Tier = model.Tier ?? (model.Verify is null ? MapTiers.Inferred : MapTiers.Verified);
        DominantType = dominantType;
        _label = model.Label;
        _kind = model.Kind;
        _detail = model.Detail;
        _x = model.X;
        _y = model.Y;
        _linksTo = model.LinksTo;
        _location = model.Location;
    }

    public MapDevice Model { get; }

    public string Id { get; }

    /// <summary>One of <see cref="MapTiers"/>. How the app knows this device's state.</summary>
    public string Tier { get; }

    /// <summary>Drives the left accent bar; null draws no bar.</summary>
    public MapConnectionType? DominantType { get; }

    public bool IsHub => Model.Hub;

    public bool OffCampus => Model.OffCampus;

    public bool IsVerifiedTier => Tier == MapTiers.Verified;

    public bool IsReported => Tier == MapTiers.Reported;

    /// <summary>Hollow: faint fill, dashed border, ring dot. "Probably fine" must not look "checked".</summary>
    public bool IsInferred => Tier == MapTiers.Inferred;

    public bool IsHuman => Tier == MapTiers.Human;

    /// <summary>REPORTED / ASK A HUMAN. Empty for tiers whose treatment says it all.</summary>
    public string TierBadge => Tier switch
    {
        MapTiers.Reported => "REPORTED",
        MapTiers.Human => "ASK A HUMAN",
        _ => string.Empty,
    };

    public bool HasTierBadge => TierBadge.Length > 0;

    /// <summary>
    /// The one gating rule, enforced here rather than in a view: only a verified, on-campus
    /// device may ever hold <c>Ready to go</c> shut. A volunteer must never face a red checklist
    /// because Facebook is having a day.
    /// </summary>
    public bool CanHoldGate => IsVerifiedTier && !OffCampus;

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    /// <summary>The 3px left accent bar: the dominant signal type's colour, or nothing.</summary>
    public IBrush? AccentBrush =>
        DominantType is { } type && Color.TryParse(type.Colour, out var colour)
            ? new SolidColorBrush(colour)
            : null;

    public bool HasAccent => AccentBrush is not null;

    /// <summary>The mock's failing-node badge: the state, in the verifier's vocabulary.</summary>
    public string FailBadge => Verify?.Kind is "hostReachable" or "internetReachable" ? "NO PING" : "DOWN";

    public bool HasLink => !string.IsNullOrWhiteSpace(LinksTo);

    public bool HasLocation => !string.IsNullOrWhiteSpace(Location);

    /// <summary>
    /// The sockets this device declares, in the author's order. Shown in the rail when the device
    /// is selected — the canvas only has room for ticks, and a port list is reference material you
    /// read once rather than something you scan at a glance.
    /// </summary>
    public IReadOnlyList<MapPort> Ports => Model.Ports;

    public bool HasPorts => Model.Ports.Count > 0;

    /// <summary>Ports currently carrying something, per edge, for the ticks drawn on the box.</summary>
    public ObservableCollection<MapPortAnchor> RightPortAnchors { get; } = new();

    public ObservableCollection<MapPortAnchor> LeftPortAnchors { get; } = new();

    /// <summary>
    /// Called by the map once it has worked out where each port sits on this box. Only ports with
    /// something plugged into them get an anchor: an empty socket is worth listing in the rail but
    /// not worth a mark on a diagram somebody is reading under pressure.
    /// </summary>
    public void SetPortAnchors(bool rightSide, IReadOnlyList<MapPortAnchor> anchors)
    {
        var target = rightSide ? RightPortAnchors : LeftPortAnchors;

        if (target.Count == anchors.Count && target.SequenceEqual(anchors))
        {
            return;
        }

        target.Clear();

        foreach (var anchor in anchors)
        {
            target.Add(anchor);
        }
    }

    public Point Centre => new(X + (BoxWidth / 2), Y + (BoxHeight / 2));

    public Point RightPort => new(X + BoxWidth, Y + (BoxHeight / 2));

    public Point LeftPort => new(X, Y + (BoxHeight / 2));

    /// <summary>
    /// What this device's dot or badge should show, by tier:
    /// verified — its own check; reported — its own check, except that a failed call renders as
    /// wait, because "we learned nothing" is different from "we know it is broken"; inferred —
    /// the upstream's status, and the view draws the dot hollow; human — nothing machine-known.
    /// A linked map's health folds in on top, so a container whose contents are broken looks it.
    /// </summary>
    public VerifyStatus EffectiveStatus
    {
        get
        {
            var own = Tier switch
            {
                MapTiers.Verified => Status,
                MapTiers.Reported => Status is VerifyStatus.Failed ? VerifyStatus.Polling : Status,
                MapTiers.Inferred => UpstreamStatus,
                _ => VerifyStatus.Unknown,
            };

            return Worst(own, LinkedStatus);
        }
    }

    /// <summary>
    /// Red treatment is a claim of knowledge, so an inferred device never gets it — the
    /// handoff's rule for a broken chain is that downstream hops go hollow, "starved", not red.
    /// Painting five boxes red hides which one actually broke.
    /// <para>
    /// The one exception is a linked map: its health comes from verified checks inside it, so a
    /// container whose contents are provably broken says so, whatever its own tier is.
    /// </para>
    /// </summary>
    public bool ShowsFailure =>
        LinkedStatus is VerifyStatus.Failed or VerifyStatus.Unsupported
        || (!IsInferred && EffectiveStatus is VerifyStatus.Failed or VerifyStatus.Unsupported);

    public bool ShowsOk => EffectiveStatus == VerifyStatus.Passed
        && (!IsInferred || LinkedStatus == VerifyStatus.Passed);

    public bool ShowsPolling => EffectiveStatus == VerifyStatus.Polling && !IsInferred;

    /// <summary>The inferred tier's ring: present, never filled, never green.</summary>
    public bool ShowsHollowDot => IsInferred;

    /// <summary>An inferred device whose upstream is broken: nothing is arriving here.</summary>
    public bool IsStarved => IsInferred
        && UpstreamStatus is VerifyStatus.Failed or VerifyStatus.Unsupported;

    /// <summary>Pushes edited values back onto the model so the map can be saved.</summary>
    public void Apply()
    {
        Model.Label = Label.Trim();
        Model.Kind = Kind;
        Model.Detail = string.IsNullOrWhiteSpace(Detail) ? null : Detail.Trim();
        Model.X = Math.Round(X);
        Model.Y = Math.Round(Y);
        Model.LinksTo = string.IsNullOrWhiteSpace(LinksTo) ? null : LinksTo.Trim();
        Model.Location = string.IsNullOrWhiteSpace(Location) ? null : Location.Trim();
    }

    protected override void StatusChanged()
    {
        OnPropertyChanged(nameof(EffectiveStatus));
        OnPropertyChanged(nameof(ShowsFailure));
        OnPropertyChanged(nameof(ShowsOk));
        OnPropertyChanged(nameof(ShowsPolling));
        OnPropertyChanged(nameof(IsStarved));
    }

    partial void OnUpstreamStatusChanged(VerifyStatus value) => OnPropertyChanged(nameof(IsStarved));
}

/// <summary>A wire: two devices, a signal type, and what the animation should be doing.</summary>
/// <summary>
/// A port that has something plugged into it, positioned on its box's edge.
/// <para>
/// <paramref name="Slot"/> is a fraction down the edge, matching the wire that lands there.
/// <paramref name="Wires"/> is how many runs share the socket — more than one is not an error, and
/// the tick grows so you can see it.
/// </para>
/// </summary>
public readonly record struct MapPortAnchor(
    string PortId,
    string Label,
    double Slot,
    int Wires,
    string Side,
    bool RightSide)
{
    /// <summary>Pixels down the box, matching <see cref="MapConnectionViewModel"/>'s edge maths.</summary>
    public double Offset => 10 + (Slot * (MapDeviceViewModel.BoxHeight - 20));

    /// <summary>Top edge of the tick, for a Canvas that positions by corner.</summary>
    public double Top => Offset - 5;

    /// <summary>Top of the click target, which is deliberately larger than the mark it covers.</summary>
    public double HitTop => Offset - 11;

    public bool IsShared => Wires > 1;

    /// <summary>Nothing plugged in. Drawn hollow, and still clickable — that is the point.</summary>
    public bool IsVacant => Wires == 0;

    /// <summary>Can a run start here? An input-only socket cannot.</summary>
    public bool CanSend => MapPortSides.AcceptsOut(Side);

    /// <summary>Can a run land here?</summary>
    public bool CanReceive => MapPortSides.AcceptsIn(Side);

    /// <summary>Both directions are genuinely open, so the operator has to say which.</summary>
    public bool IsAmbiguous => Side == MapPortSides.Both;

    public string Tooltip => Wires switch
    {
        0 => $"{Label} — nothing plugged in. Click to wire it.",
        1 => Label,
        _ => $"{Label} — {Wires} runs share this socket",
    };
}

public sealed partial class MapConnectionViewModel : MapProbeViewModel
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isDimmed;

    public MapConnectionViewModel(
        MapConnection model,
        MapDeviceViewModel from,
        MapDeviceViewModel to,
        MapConnectionType type,
        VerifierRegistry registry)
        : base(model.Verify, model.CheckSteps, registry)
    {
        Model = model;
        From = from;
        To = to;
        Type = type;

        // Per-wire speed jitter, stable across restarts via the stored seed. Identical durations
        // make the whole map throb in lockstep — "alive" versus "loading spinner".
        var jitter = (model.FlowSeed % 997) / 997.0;
        FlowSeconds = type.Wireless
            ? 6.2 + (jitter * 1.0)
            : Math.Clamp(type.FlowSeconds + ((jitter - 0.5) * 1.6), 3.0, 5.6);

        from.PropertyChanged += OnEndChanged;
        to.PropertyChanged += OnEndChanged;
    }

    public MapConnection Model { get; }

    public MapDeviceViewModel From { get; }

    public MapDeviceViewModel To { get; }

    public MapConnectionType Type { get; }

    /// <summary>This wire's own cycle time, seconds. The view's clock divides by this.</summary>
    public double FlowSeconds { get; }

    /// <summary>
    /// Where this wire meets each box, as a fraction down that edge. Assigned by the map once it
    /// knows how many wires share the edge.
    /// <para>
    /// The handoff says many wires converging on one point is correct and desirable, and at four
    /// or five it is. At ten - a church X32 with a digital snake, wireless receivers, mains, subs,
    /// IEM sends, a network trunk and an analog snake - they become one rope and you cannot tell
    /// which strand died. Fanning across the edge keeps each followable while still reading as a
    /// patch panel.
    /// </para>
    /// </summary>
    public double FromSlot { get; set; } = 0.5;

    public double ToSlot { get; set; } = 0.5;

    public string? Label => Model.Label;

    public int? LengthFt => Model.LengthFt;

    public bool IsStandby => Model.Standby;

    /// <summary>One cable, traffic both ways. Drawn with flow drifting in both directions.</summary>
    public bool IsBidirectional => Model.Bidirectional;

    /// <summary>
    /// The <see cref="MapPort"/> each end lands on, when the run names one. Null is the common
    /// case and means "spread it along the edge with everything else".
    /// </summary>
    public MapPort? FromPortSpec => Find(From, Model.FromPort);

    public MapPort? ToPortSpec => Find(To, Model.ToPort);

    private static MapPort? Find(MapDeviceViewModel device, string? portId) =>
        string.IsNullOrEmpty(portId)
            ? null
            : device.Model.Ports.FirstOrDefault(p => p.Id == portId);

    /// <summary>
    /// What to print at each end in the rail: <c>AES50 A → CH 25-26</c>. Empty when neither end
    /// names a port, which keeps the rail quiet for maps that never adopted them.
    /// </summary>
    public string PortRoute
    {
        get
        {
            var from = FromPortSpec?.Label;
            var to = ToPortSpec?.Label;

            if (from is null && to is null)
            {
                return string.Empty;
            }

            var arrow = IsBidirectional ? "↔" : "→";
            return $"{from ?? "—"} {arrow} {to ?? "—"}";
        }
    }

    public bool HasPortRoute => PortRoute.Length > 0;

    /// <summary>
    /// live — flowing dashes in the type's colour; standby — grey, still, by design;
    /// down — this wire's own check failed: the reserved red alarm pattern;
    /// starved — nothing arriving because upstream is broken: faint, still, not red,
    /// because a starved hop is not a broken hop and drawing five red boxes hides the cause.
    /// </summary>
    public string FlowState
    {
        get
        {
            if (IsStandby)
            {
                return "standby";
            }

            if (HasVerify && IsFailed)
            {
                return "down";
            }

            // A one-way run starves when its source dies. A two-way run starves when *either* end
            // does, because half a conversation is not a working link — an AES50 snake with a dead
            // console still has a live stage box and carries nothing anybody wants.
            if (From.ShowsFailure || From.IsStarved)
            {
                return "starved";
            }

            if (IsBidirectional && (To.ShowsFailure || To.IsStarved))
            {
                return "starved";
            }

            return "live";
        }
    }

    public bool IsDown => FlowState == "down";

    /// <summary>Where the wire leaves and arrives: facing edges, vertical centre.</summary>
    /// <summary>The point on an edge for a given slot fraction.</summary>
    private static Point EdgePoint(MapDeviceViewModel device, bool rightSide, double slot)
    {
        // Inset so a fan never lands exactly on a rounded corner.
        var y = device.Y + 10 + (slot * (MapDeviceViewModel.BoxHeight - 20));
        return new Point(rightSide ? device.X + MapDeviceViewModel.BoxWidth : device.X, y);
    }

    public (Point Start, Point End) Ports()
    {
        var forward = To.Centre.X >= From.Centre.X;
        return forward
            ? (EdgePoint(From, true, FromSlot), EdgePoint(To, false, ToSlot))
            : (EdgePoint(From, false, FromSlot), EdgePoint(To, true, ToSlot));
    }

    /// <summary>
    /// The handoff's curve: cubic bézier with horizontal control points, so wires converging on
    /// a hub read as a patch panel. Same-device loops bow out to the left on purpose.
    /// </summary>
    public Geometry Geometry
    {
        get
        {
            var (start, end) = Ports();
            var reach = Math.Max(52, Math.Abs(end.X - start.X) * 0.45);

            var geometry = new StreamGeometry();
            using var context = geometry.Open();
            context.BeginFigure(start, isFilled: false);
            context.CubicBezierTo(new Point(start.X + reach, start.Y), new Point(end.X - reach, end.Y), end);
            context.EndFigure(false);
            return geometry;
        }
    }

    public Point Midpoint
    {
        get
        {
            var (start, end) = Ports();
            var reach = Math.Max(52, Math.Abs(end.X - start.X) * 0.45);
            var c1 = new Point(start.X + reach, start.Y);
            var c2 = new Point(end.X - reach, end.Y);

            // A cubic evaluated at t = 0.5 reduces to this.
            return new Point(
                (start.X + (3 * c1.X) + (3 * c2.X) + end.X) / 8,
                (start.Y + (3 * c1.Y) + (3 * c2.Y) + end.Y) / 8);
        }
    }

    /// <summary>Rail card title: <c>Cam 3 · Balcony → vMix</c>, or <c>↔</c> when it runs both ways.</summary>
    public string Title => $"{From.Label} {(IsBidirectional ? "↔" : "→")} {To.Label}";

    /// <summary>Tells the view the curve moved - after a re-fan, or an endpoint moving.</summary>
    public void RefreshGeometry()
    {
        OnPropertyChanged(nameof(Geometry));
        OnPropertyChanged(nameof(Midpoint));
    }

    private void OnEndChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MapDeviceViewModel.X)
            or nameof(MapDeviceViewModel.Y)
            or nameof(MapDeviceViewModel.Centre))
        {
            OnPropertyChanged(nameof(Geometry));
            OnPropertyChanged(nameof(Midpoint));
        }

        if (e.PropertyName is nameof(MapDeviceViewModel.ShowsFailure)
            or nameof(MapDeviceViewModel.IsStarved))
        {
            OnPropertyChanged(nameof(FlowState));
            OnPropertyChanged(nameof(IsDown));
        }
    }

    protected override void StatusChanged()
    {
        OnPropertyChanged(nameof(FlowState));
        OnPropertyChanged(nameof(IsDown));
    }
}
