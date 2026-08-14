using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SundayReady.Models;

namespace SundayReady.ViewModels;

/// <summary>One line of a station card's body: a glyph and a label, nothing more.</summary>
public sealed class TechdeskPreviewItemViewModel
{
    public TechdeskPreviewItemViewModel(SnapshotItem item)
    {
        IsDone = item.State == SnapshotItemStates.Done;
        Glyph = IsDone ? "✓" : "○";
        Label = item.Label;
    }

    public string Glyph { get; }

    public bool IsDone { get; }

    public string Label { get; }
}

/// <summary>
/// One station as the techdesk sees it: the last snapshot it published, aged against the
/// clock. Updated in place rather than rebuilt each sweep — this board lives on a wall and
/// nothing on it should move unless the thing it describes moved.
/// </summary>
public sealed partial class TechdeskStationViewModel : ObservableObject
{
    private readonly Action<TechdeskStationViewModel> _page;
    private readonly Action<TechdeskStationViewModel> _markNotStaffed;

    private StationSnapshot _snapshot = new();
    private TimeSpan _silenceAfter = TimeSpan.FromMinutes(22);
    private DateTimeOffset _now = DateTimeOffset.Now;
    private bool _acceptedNotStaffed;

    /// <summary>Answers a button press that has no channel behind it yet. Usually null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActionStatus))]
    private string? _actionStatus;

    public TechdeskStationViewModel(
        string key,
        Action<TechdeskStationViewModel> page,
        Action<TechdeskStationViewModel> markNotStaffed)
    {
        Key = key;
        _page = page;
        _markNotStaffed = markNotStaffed;
    }

    public string Key { get; }

    public StationSnapshot Snapshot => _snapshot;

    public ObservableCollection<TechdeskPreviewItemViewModel> Preview { get; } = new();

    public void Update(StationSnapshot snapshot, TimeSpan silenceAfter, DateTimeOffset now, bool acceptedNotStaffed)
    {
        _snapshot = snapshot;
        _silenceAfter = silenceAfter;
        _now = now;
        _acceptedNotStaffed = acceptedNotStaffed;

        RebuildPreview();
        RaiseAll();
    }

    // ---- State ----

    public TimeSpan Silence => _now - _snapshot.LastHeartbeat;

    public bool IsSilent => _acceptedNotStaffed || Silence > _silenceAfter;

    public bool IsFailing => !IsSilent && _snapshot.Failing > 0;

    public bool IsReady => !IsSilent && !IsFailing && _snapshot.Total > 0 && _snapshot.Completed >= _snapshot.Total;

    public bool IsInProgress => !IsSilent && !IsFailing && !IsReady;

    /// <summary>The card header, its dot and the 1d tile all tint off these three.</summary>
    public bool IsOkTinted => IsReady;

    public bool IsFailTinted => IsFailing;

    public bool IsNeutralTinted => IsSilent;

    // ---- Header ----

    public string Name => _snapshot.Station;

    public string HostLine =>
        $"{_snapshot.Host} · {(string.IsNullOrWhiteSpace(_snapshot.Operator) ? "UNASSIGNED" : _snapshot.Operator.ToUpperInvariant())}";

    public string CountText => IsSilent ? "—" : $"{_snapshot.Completed}/{_snapshot.Total}";

    public string PercentText => IsSilent ? "—" : $"{_snapshot.Percentage}%";

    public string StatusText
    {
        get
        {
            if (_acceptedNotStaffed)
            {
                return "NOT STAFFED TODAY";
            }

            if (IsSilent)
            {
                return $"NO HEARTBEAT {Minutes(Silence)} MIN";
            }

            if (IsFailing)
            {
                return _snapshot.Failing == 1 ? "1 FAILING" : $"{_snapshot.Failing} FAILING";
            }

            if (IsReady)
            {
                return _snapshot.ReadyAt is { } ready ? $"READY {ready:h:mm tt}".ToUpperInvariant() : "READY";
            }

            var left = _snapshot.Total - _snapshot.Completed;
            return $"{left} LEFT";
        }
    }

    /// <summary>1d's tile has room for the operator but not for a ready timestamp.</summary>
    public string BoardStatusText
    {
        get
        {
            if (IsSilent)
            {
                return StatusText;
            }

            var status = IsReady ? "READY" : StatusText;
            var who = string.IsNullOrWhiteSpace(_snapshot.Operator) ? "UNASSIGNED" : _snapshot.Operator.ToUpperInvariant();
            return $"{status} · {who}";
        }
    }

    /// <summary>Zero while a station is silent: its last known progress is not news any more.</summary>
    public double CompletedFraction =>
        IsSilent || _snapshot.Total == 0 ? 0 : (double)_snapshot.Completed / _snapshot.Total;

    public double FailingFraction =>
        IsSilent || _snapshot.Total == 0 ? 0 : (double)_snapshot.Failing / _snapshot.Total;

    // ---- Body ----

    public bool HasCallout => IsFailing && FirstFailure is not null;

    private SnapshotItem? FirstFailure =>
        _snapshot.Items.FirstOrDefault(i => i.State == SnapshotItemStates.Failing);

    public string CalloutTitle => FirstFailure?.Label ?? string.Empty;

    public string CalloutDetail
    {
        get
        {
            if (FirstFailure is not { } failure)
            {
                return string.Empty;
            }

            var since = failure.FailingSince is { } started
                ? $"Failing {Minutes(_now - started)} min"
                : "Failing";

            return failure.LastPassAt is { } passed
                ? $"{since} · last good {passed:h:mm tt}"
                : $"{since} · never passed today";
        }
    }

    public string MoreText { get; private set; } = string.Empty;

    public bool HasMore => !string.IsNullOrEmpty(MoreText);

    public string SilentExplanation =>
        _acceptedNotStaffed
            ? $"Accepted as not staffed for today. Last heard from at {_snapshot.LastHeartbeat:h:mm tt}."
            : $"Station hasn't reported since {_snapshot.LastHeartbeat:h:mm tt}. Either the PC is off or nobody has sat down.";

    public bool ShowNotStaffedActions => IsSilent && !_acceptedNotStaffed;

    public bool HasActionStatus => !string.IsNullOrEmpty(ActionStatus);

    // ---- Actions ----

    [RelayCommand]
    private void Page() => _page(this);

    [RelayCommand]
    private void MarkNotStaffed() => _markNotStaffed(this);

    private void RebuildPreview()
    {
        Preview.Clear();

        if (IsSilent)
        {
            MoreText = string.Empty;
            return;
        }

        // The failing item already has the callout above; listing it again wastes a row.
        var rest = _snapshot.Items.Where(i => i.State != SnapshotItemStates.Failing).ToList();
        var shown = Math.Min(rest.Count, HasCallout ? 4 : 5);

        foreach (var item in rest.Take(shown))
        {
            Preview.Add(new TechdeskPreviewItemViewModel(item));
        }

        var remaining = rest.Skip(shown).ToList();
        MoreText = remaining.Count == 0
            ? string.Empty
            : remaining.All(i => i.State == SnapshotItemStates.Done)
                ? $"+ {remaining.Count} more complete"
                : $"+ {remaining.Count} more";
    }

    private static int Minutes(TimeSpan span) => Math.Max(0, (int)span.TotalMinutes);

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(IsSilent));
        OnPropertyChanged(nameof(IsFailing));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(IsInProgress));
        OnPropertyChanged(nameof(IsOkTinted));
        OnPropertyChanged(nameof(IsFailTinted));
        OnPropertyChanged(nameof(IsNeutralTinted));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(HostLine));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(PercentText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(BoardStatusText));
        OnPropertyChanged(nameof(CompletedFraction));
        OnPropertyChanged(nameof(FailingFraction));
        OnPropertyChanged(nameof(HasCallout));
        OnPropertyChanged(nameof(CalloutTitle));
        OnPropertyChanged(nameof(CalloutDetail));
        OnPropertyChanged(nameof(MoreText));
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(SilentExplanation));
        OnPropertyChanged(nameof(ShowNotStaffedActions));
    }
}
