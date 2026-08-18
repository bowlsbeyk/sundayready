using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SundayReady.Models;
using SundayReady.Services;

namespace SundayReady.ViewModels;

/// <summary>
/// Anything on a map that can be checked — a box or a line.
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

/// <summary>A box on the map.</summary>
public sealed partial class MapComponentViewModel : MapProbeViewModel
{
    /// <summary>Box size. Fixed, so connection geometry can be computed without a layout pass.</summary>
    public const double Width = 184;

    public const double Height = 68;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Centre), nameof(RightPort), nameof(LeftPort))]
    private double _x;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Centre), nameof(RightPort), nameof(LeftPort))]
    private double _y;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Health of the map this box links to, folded into <see cref="EffectiveStatus"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveStatus), nameof(ShowsFailure), nameof(ShowsOk))]
    private VerifyStatus _linkedStatus = VerifyStatus.Unknown;

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string _kind = MapComponentKinds.Device;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLink))]
    private string? _linksTo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLocation))]
    private string? _location;

    public MapComponentViewModel(MapComponent model, VerifierRegistry registry)
        : base(model.Verify, model.CheckSteps, registry)
    {
        Model = model;
        Id = model.Id;
        _label = model.Label;
        _kind = model.Kind;
        _x = model.X;
        _y = model.Y;
        _linksTo = model.LinksTo;
        _location = model.Location;
    }

    public MapComponent Model { get; }

    public string Id { get; }

    public bool HasLink => !string.IsNullOrWhiteSpace(LinksTo);

    public bool HasLocation => !string.IsNullOrWhiteSpace(Location);

    public Point Centre => new(X + (Width / 2), Y + (Height / 2));

    /// <summary>Where a connection leaves this box, and where one arrives.</summary>
    public Point RightPort => new(X + Width, Y + (Height / 2));

    public Point LeftPort => new(X, Y + (Height / 2));

    /// <summary>
    /// The worse of this box's own check and the map it links to. A container whose contents are
    /// broken has to look broken, or drilling down would be the only way to find anything.
    /// </summary>
    public VerifyStatus EffectiveStatus => Worst(Status, LinkedStatus);

    public bool ShowsFailure => EffectiveStatus is VerifyStatus.Failed or VerifyStatus.Unsupported;

    public bool ShowsOk => EffectiveStatus == VerifyStatus.Passed;

    public bool ShowsPolling => EffectiveStatus == VerifyStatus.Polling;

    /// <summary>Pushes edited values back onto the model so the map can be saved.</summary>
    public void Apply()
    {
        Model.Label = Label.Trim();
        Model.Kind = Kind;
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
    }
}

/// <summary>A line between two boxes, and the signal it claims to carry.</summary>
public sealed partial class MapConnectionViewModel : MapProbeViewModel
{
    [ObservableProperty]
    private bool _isSelected;

    public MapConnectionViewModel(
        MapConnection model,
        MapComponentViewModel from,
        MapComponentViewModel to,
        VerifierRegistry registry)
        : base(model.Verify, model.CheckSteps, registry)
    {
        Model = model;
        From = from;
        To = to;

        // The line follows the boxes, so it is rebuilt whenever either end moves or changes state.
        from.PropertyChanged += OnEndChanged;
        to.PropertyChanged += OnEndChanged;
    }

    public MapConnection Model { get; }

    public MapComponentViewModel From { get; }

    public MapComponentViewModel To { get; }

    public string? Label => Model.Label;

    public bool HasLabel => !string.IsNullOrWhiteSpace(Model.Label);

    /// <summary>
    /// A cubic curve rather than a straight line. Straight lines between boxes on a grid overlap
    /// each other and become impossible to follow; a curve that leaves horizontally and arrives
    /// horizontally reads as signal flow and separates naturally.
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

    /// <summary>Midpoint of the curve, where the label sits.</summary>
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

    public double LabelLeft => Midpoint.X - (LabelWidth / 2);

    public double LabelTop => Midpoint.Y - 14;

    public static double LabelWidth => 150;

    /// <summary>
    /// Dashes only travel when the signal is believed to be arriving. Movement means working, and
    /// a line that stops moving is the thing your eye catches from across the room.
    /// </summary>
    public bool IsFlowing => HasVerify
        ? Status == VerifyStatus.Passed
        : !From.ShowsFailure && !To.ShowsFailure;

    private (Point Start, Point End) Ports()
    {
        // Leave from whichever side faces the target, so a box wired right-to-left does not draw
        // a curve looping back through itself.
        var forward = To.Centre.X >= From.Centre.X;
        return forward
            ? (From.RightPort, To.LeftPort)
            : (From.LeftPort, To.RightPort);
    }

    private void OnEndChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MapComponentViewModel.X)
            or nameof(MapComponentViewModel.Y)
            or nameof(MapComponentViewModel.Centre))
        {
            OnPropertyChanged(nameof(Geometry));
            OnPropertyChanged(nameof(Midpoint));
            OnPropertyChanged(nameof(LabelLeft));
            OnPropertyChanged(nameof(LabelTop));
        }

        if (e.PropertyName is nameof(MapComponentViewModel.ShowsFailure))
        {
            OnPropertyChanged(nameof(IsFlowing));
        }
    }

    protected override void StatusChanged() => OnPropertyChanged(nameof(IsFlowing));
}
