using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SundayReady.Models;
using SundayReady.Services;

namespace SundayReady.ViewModels;

/// <summary>One chip of the 1c DEVICES grid, or one line of the 1d DEVICE MAP.</summary>
public sealed class TechdeskDeviceViewModel
{
    public TechdeskDeviceViewModel(string name, string detail, bool isUp, bool isIdle)
    {
        Name = name;
        Detail = detail;
        IsUp = isUp;
        IsIdle = isIdle;
    }

    public string Name { get; }

    public string Detail { get; }

    public bool IsUp { get; }

    public bool IsIdle { get; }

    public bool IsDown => !IsUp && !IsIdle;
}

/// <summary>One row of the 1d DEVICE MAP: several devices sharing a status.</summary>
public sealed class TechdeskDeviceGroupViewModel
{
    public TechdeskDeviceGroupViewModel(string names, string detail, bool isUp, bool isIdle)
    {
        Names = names;
        Detail = detail;
        IsUp = isUp;
        IsIdle = isIdle;
    }

    public string Names { get; }

    public string Detail { get; }

    public bool IsUp { get; }

    public bool IsIdle { get; }

    public bool IsDown => !IsUp && !IsIdle;
}

/// <summary>
/// One row of 1d's NEEDS A HUMAN panel. Completed items never appear here — the panel exists
/// to be short.
/// </summary>
public sealed partial class TechdeskExceptionViewModel : ObservableObject
{
    private readonly Action<TechdeskExceptionViewModel> _act;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActionStatus))]
    private string? _actionStatus;

    public TechdeskExceptionViewModel(
        string kind,
        string glyph,
        string headline,
        string detail,
        string actionLabel,
        TechdeskStationViewModel station,
        Action<TechdeskExceptionViewModel> act)
    {
        Kind = kind;
        Glyph = glyph;
        Headline = headline;
        Detail = detail;
        ActionLabel = actionLabel;
        Station = station;
        _act = act;
    }

    public string Kind { get; }

    public string Glyph { get; }

    public string Headline { get; }

    public string Detail { get; }

    public string ActionLabel { get; }

    public TechdeskStationViewModel Station { get; }

    public bool IsFail => Kind == ExceptionKinds.Fail;

    public bool IsUnknown => Kind == ExceptionKinds.Unknown;

    public bool IsWait => Kind == ExceptionKinds.Wait;

    public bool HasActionStatus => !string.IsNullOrEmpty(ActionStatus);

    [RelayCommand]
    private void Act() => _act(this);
}

public static class ExceptionKinds
{
    public const string Fail = "fail";
    public const string Unknown = "unknown";
    public const string Wait = "wait";
}

/// <summary>
/// The tech director's screen. Reads every station's snapshot off the share, ages each one
/// against the clock, and reduces the result to what somebody has to do something about.
/// <para>
/// Two layouts are built from this one view model — 1c's station columns and 1d's wall
/// board — chosen by <c>techdeskLayout</c> in <c>station.json</c>. The handoff asked for a
/// choice between them; which one wins depends on whether the techdesk display is worked in
/// or glanced at, and that is only answerable in the room.
/// </para>
/// </summary>
public sealed partial class TechdeskViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// How many exception rows fit the 1d panel at its design row height. Past this the panel
    /// summarises — a wall board that has to be scrolled is not a wall board.
    /// </summary>
    private const int MaxExceptions = 4;

    /// <summary>Rolling window behind the sparkline: 60 minutes at one sweep every 15 seconds.</summary>
    private const int SampleWindow = 240;

    private readonly SnapshotStore _snapshots;
    private readonly TechdeskDayStore _dayStore;
    private readonly ProcessLauncher _launcher;
    private readonly StationConfig _config;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _sweepTimer;
    private readonly Dictionary<string, TechdeskStationViewModel> _byKey = new(StringComparer.OrdinalIgnoreCase);

    private TechdeskDay _day;
    private DateTime? _serviceStart;
    private bool _disposed;

    [ObservableProperty]
    private string _clock = string.Empty;

    [ObservableProperty]
    private string _countdown = "—";

    [ObservableProperty]
    private string _lastSweep = string.Empty;

    private readonly ChecklistLoader? _checklists;
    private readonly StationConfigLoader? _stationLoader;
    private readonly VerifierRegistry? _registry;

    public TechdeskViewModel(
        StationConfig config,
        ProcessLauncher launcher,
        ChecklistLoader? checklists = null,
        StationConfigLoader? stationLoader = null,
        VerifierRegistry? registry = null)
    {
        _config = config;
        _launcher = launcher;
        _checklists = checklists;
        _stationLoader = stationLoader;
        _registry = registry;
        _snapshots = new SnapshotStore(config.TechdeskShare);
        _dayStore = new TechdeskDayStore();
        _day = _dayStore.Load(DateOnly.FromDateTime(DateTime.Now));

        _serviceStart = ParseTime(config.Service?.StartsAt);

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => Tick();

        _sweepTimer = new DispatcherTimer { Interval = SnapshotStore.PublishInterval };
        _sweepTimer.Tick += (_, _) => Sweep();

        // The techdesk watches the shared map for the same reason it watches stations: it is
        // the room's one screen. Only when a registry exists — a headless techdesk cannot poll.
        if (registry is not null)
        {
            _mapWatch = new MapWorkspaceViewModel(new SystemMapStore(config.TechdeskShare), registry);
        }

        Tick();
        Sweep();
    }

    private readonly MapWorkspaceViewModel? _mapWatch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMapAlert))]
    private string _mapAlert = string.Empty;

    [ObservableProperty]
    private bool _mapAlertIsFail;

    public bool HasMapAlert => MapAlert.Length > 0;

    public bool CanOpenMap => _registry is not null;

    /// <summary>A fresh workspace for the MAP window; the banner watcher stays untouched.</summary>
    public MapWorkspaceViewModel? CreateMapWorkspace() =>
        _registry is null ? null : new(new SystemMapStore(_config.TechdeskShare), _registry);

    /// <summary>
    /// Off-campus trouble surfaces here — as a line on the techdesk, never as a volunteer's red
    /// checklist. On-campus breaks show here too, because whoever watches this screen is whoever
    /// walks over and fixes them.
    /// </summary>
    private void RefreshMapAlert()
    {
        if (_mapWatch is null)
        {
            return;
        }

        var alert = _mapWatch.MapAlert();
        MapAlert = alert?.Text ?? string.Empty;
        MapAlertIsFail = alert?.IsFail ?? false;
    }

    /// <summary>
    /// A PC in techdesk mode shows nothing else, so without this the only way out of the mode
    /// — or into any other setting — would be hand-editing station.json. Null when the
    /// techdesk was opened as a window from a station, which has its own way to Settings.
    /// </summary>
    public bool CanOpenSettings => _checklists is not null && _stationLoader is not null && _registry is not null;

    public SettingsViewModel? CreateSettings() =>
        _checklists is not null && _stationLoader is not null && _registry is not null
            ? new SettingsViewModel(_config, _checklists, _stationLoader, _registry)
            : null;

    public ObservableCollection<TechdeskStationViewModel> Stations { get; } = new();

    public ObservableCollection<TechdeskExceptionViewModel> Exceptions { get; } = new();

    public ObservableCollection<TechdeskDeviceViewModel> Devices { get; } = new();

    public ObservableCollection<TechdeskDeviceGroupViewModel> DeviceGroups { get; } = new();

    /// <summary>Measured round trips, oldest first. The only telemetry the app actually has.</summary>
    public ObservableCollection<int> PingSamples { get; } = new();

    public bool IsColumnsLayout =>
        !string.Equals(_config.TechdeskLayout, TechdeskLayouts.Board, StringComparison.OrdinalIgnoreCase);

    public bool IsBoardLayout => !IsColumnsLayout;

    public string Version => AppVersion.Display;

    public string ShareLine => $"SHARE {_snapshots.Directory}";

    public void Start()
    {
        _clockTimer.Start();
        _sweepTimer.Start();
        _mapWatch?.Start();
    }

    // ---- Header ----

    public string DateLine
    {
        get
        {
            var date = DateTime.Now.ToString("ddd dd MMM yyyy").ToUpperInvariant();
            return _config.Service?.StartsAt is { } starts && TimeOnly.TryParse(starts, out var time)
                ? $"{date} · {time:HH:mm} SERVICE"
                : date;
        }
    }

    public string Kicker => $"SUNDAYREADY TECHDESK · {DateTime.Now:ddd dd MMM}".ToUpperInvariant();

    /// <summary>1d's right-hand mono line: the three times that matter, in order.</summary>
    public string TimesLine
    {
        get
        {
            var parts = new List<string>();
            Add(parts, "DOORS", _config.Service?.DoorsAt);
            Add(parts, "SERVICE", _config.Service?.StartsAt);
            Add(parts, "STREAM", _config.Service?.StreamAt);
            return parts.Count == 0 ? "NO SERVICE TIMES SET" : string.Join(" · ", parts);

            static void Add(List<string> parts, string label, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value) && TimeOnly.TryParse(value, out var time))
                {
                    parts.Add($"{label} {time:HH:mm}");
                }
            }
        }
    }

    public int ReadyCount => Stations.Count(s => s.IsReady);

    public bool AnyFailing => Stations.Any(s => s.IsFailing);

    public string SummaryText => Stations.Count == 0
        ? "NO STATIONS REPORTING"
        : $"{ReadyCount} OF {Stations.Count} STATIONS READY";

    public bool SummaryIsOk => Stations.Count > 0 && ReadyCount == Stations.Count;

    public bool SummaryIsFailing => AnyFailing || Stations.Count == 0;

    /// <summary>1d's 66px verdict. The one thing readable from the back of the room.</summary>
    public string Verdict
    {
        get
        {
            if (Stations.Count == 0) return "No stations reporting";
            if (ReadyCount == Stations.Count) return "Ready";
            if (ReadyCount == 0) return "Not ready";
            return "Almost ready";
        }
    }

    // ---- Exceptions panel ----

    [ObservableProperty]
    private string _exceptionCountText = "0 ITEMS";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverflow))]
    private string _overflowText = string.Empty;

    public bool HasOverflow => !string.IsNullOrEmpty(OverflowText);

    [ObservableProperty]
    private string _doneFooter = string.Empty;

    // ---- Telemetry rail ----

    /// <summary>
    /// Nothing measures throughput, so it stays an em-dash. Inventing a number here would be
    /// the one thing on a tech director's screen that could get a service streamed at 2 Mb/s
    /// while the board says everything is fine.
    /// </summary>
    public string DownText => "—";

    public string UpText => "—";

    public string DropsText => "—";

    public string PingText => PingSamples.Count == 0 ? "—" : PingSamples[^1].ToString();

    public bool HasPing => PingSamples.Count > 0;

    public bool HasSamples => PingSamples.Count > 1;

    public string SparklineCaption => HasSamples
        ? "PING FROM STATIONS, LAST 60 MIN · NO THROUGHPUT FEED"
        : "NO UPLINK FEED CONFIGURED";

    /// <summary>Viewer counts come from the YouTube and Facebook live APIs, which the app has
    /// no credentials for. Optional feed, never gate input — so, tiles with an em-dash.</summary>
    public string YouTubeCount => "—";

    public string FacebookCount => "—";

    public string TotalWatching => "—";

    public string PeakLastWeek => "—";

    /// <summary>1d puts the peak on its own line, where a bare em-dash reads as a stray rule.</summary>
    public string PeakLine => "PEAK — LAST WEEK";

    public string SweepLine => $"POLL EVERY 15s · LAST SWEEP {LastSweep}";

    // ---- Loop ----

    private void Tick()
    {
        Clock = DateTime.Now.ToString("h:mm");

        if (_serviceStart is not { } start)
        {
            Countdown = "—";
            return;
        }

        var remaining = start - DateTime.Now;
        if (remaining <= TimeSpan.Zero)
        {
            Countdown = "NOW";
            return;
        }

        // Over an hour formats H:MM:SS. At 84px a three-digit minute count is unreadable and
        // collides with its label.
        Countdown = remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : $"{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    private void Sweep()
    {
        RefreshMapAlert();

        if (_disposed)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var silenceAfter = TimeSpan.FromMinutes(Math.Max(1, _config.TechdeskHeartbeatMinutes));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in _snapshots.ReadAll())
        {
            var key = TechdeskDayStore.KeyFor(snapshot.Host, snapshot.Station);
            seen.Add(key);

            if (!_byKey.TryGetValue(key, out var station))
            {
                station = new TechdeskStationViewModel(key, Page, MarkNotStaffed);
                _byKey[key] = station;
                Stations.Add(station);
            }

            station.Update(snapshot, silenceAfter, now, _day.NotStaffed.Contains(key));
        }

        // A station whose file was deleted mid-morning. Drop it rather than freezing its card.
        foreach (var stale in _byKey.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            Stations.Remove(_byKey[stale]);
            _byKey.Remove(stale);
        }

        SortStations();
        SampleUplink();
        RebuildDevices(now, silenceAfter);
        RebuildExceptions(now);

        LastSweep = DateTime.Now.ToString("h:mm");

        OnPropertyChanged(nameof(ReadyCount));
        OnPropertyChanged(nameof(AnyFailing));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(SummaryIsOk));
        OnPropertyChanged(nameof(SummaryIsFailing));
        OnPropertyChanged(nameof(Verdict));
        OnPropertyChanged(nameof(PingText));
        OnPropertyChanged(nameof(HasPing));
        OnPropertyChanged(nameof(HasSamples));
        OnPropertyChanged(nameof(SparklineCaption));
        OnPropertyChanged(nameof(SweepLine));
        OnPropertyChanged(nameof(DateLine));
        OnPropertyChanged(nameof(Kicker));
    }

    /// <summary>
    /// Stable order: the board must not reshuffle when a station ticks an item. Name order,
    /// which is whatever the church called them.
    /// </summary>
    private void SortStations()
    {
        var ordered = Stations.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            var current = Stations.IndexOf(ordered[index]);
            if (current != index)
            {
                Stations.Move(current, index);
            }
        }
    }

    private void SampleUplink()
    {
        var pings = Stations
            .Where(s => !s.IsSilent && s.Snapshot.PingMs is > 0)
            .Select(s => s.Snapshot.PingMs!.Value)
            .ToList();

        if (pings.Count == 0)
        {
            return;
        }

        PingSamples.Add(pings.Max());

        while (PingSamples.Count > SampleWindow)
        {
            PingSamples.RemoveAt(0);
        }
    }

    /// <summary>
    /// The devices the app can honestly speak for: the station PCs themselves, which prove
    /// they are alive by writing a heartbeat. Cameras and consoles are only visible through
    /// whatever a station's verifiers happen to check, so they are not listed as devices.
    /// </summary>
    private void RebuildDevices(DateTimeOffset now, TimeSpan silenceAfter)
    {
        Devices.Clear();
        DeviceGroups.Clear();

        foreach (var station in Stations)
        {
            var silence = now - station.Snapshot.LastHeartbeat;
            var up = silence <= SnapshotStore.PublishInterval * 4;
            var idle = !up && silence <= silenceAfter;

            var detail = up
                ? LastOctet(station.Snapshot.Address) ?? "UP"
                : idle ? "IDLE" : "NO DATA";

            Devices.Add(new TechdeskDeviceViewModel(station.Snapshot.Host, detail, up, idle));
        }

        foreach (var group in Devices.GroupBy(d => d.IsUp ? 2 : d.IsIdle ? 1 : 0).OrderByDescending(g => g.Key))
        {
            var members = group.ToList();
            var detail = group.Key switch
            {
                2 => $"{members.Count} UP",
                1 => "IDLE",
                _ => "NO DATA",
            };

            DeviceGroups.Add(new TechdeskDeviceGroupViewModel(
                string.Join(" · ", members.Select(m => m.Name)),
                detail,
                group.Key == 2,
                group.Key == 1));
        }
    }

    private void RebuildExceptions(DateTimeOffset now)
    {
        Exceptions.Clear();

        var rows = new List<TechdeskExceptionViewModel>();

        foreach (var station in Stations.Where(s => s.IsSilent))
        {
            rows.Add(new TechdeskExceptionViewModel(
                ExceptionKinds.Unknown,
                "?",
                $"{station.Name} station never checked in",
                $"{station.HostLine} · LAST HEARTBEAT {station.Snapshot.LastHeartbeat:h:mm tt}".ToUpperInvariant(),
                "Page volunteer",
                station,
                Act));
        }

        foreach (var station in Stations.Where(s => !s.IsSilent))
        {
            foreach (var item in station.Snapshot.Items.Where(i => i.State == SnapshotItemStates.Failing))
            {
                var since = item.FailingSince is { } started
                    ? $"FAILING {Math.Max(0, (int)(now - started).TotalMinutes)} MIN"
                    : "FAILING";

                var lastGood = item.LastPassAt is { } passed
                    ? $"LAST GOOD {passed:h:mm tt}"
                    : "NEVER PASSED TODAY";

                rows.Add(new TechdeskExceptionViewModel(
                    ExceptionKinds.Fail,
                    "!",
                    item.Label,
                    $"{item.Tab} · {since} · {lastGood}".ToUpperInvariant(),
                    "Page volunteer",
                    station,
                    Act));
            }
        }

        foreach (var station in Stations.Where(s => !s.IsSilent))
        {
            foreach (var item in station.Snapshot.Items.Where(i =>
                         i.State is SnapshotItemStates.Open or SnapshotItemStates.Polling))
            {
                var kind = item.Type switch
                {
                    ChecklistItemTypes.Verified => "AUTO ITEM",
                    ChecklistItemTypes.Action => "ACTION ITEM",
                    _ => "MANUAL ITEM",
                };

                rows.Add(new TechdeskExceptionViewModel(
                    ExceptionKinds.Wait,
                    "○",
                    item.Label,
                    $"{item.Tab} · {kind} · {(item.State == SnapshotItemStates.Polling ? "POLLING" : "UNCHECKED")}".ToUpperInvariant(),
                    "Nudge",
                    station,
                    Act));
            }
        }

        foreach (var row in rows.Take(MaxExceptions))
        {
            Exceptions.Add(row);
        }

        var hidden = rows.Count - Exceptions.Count;
        OverflowText = hidden <= 0 ? string.Empty : $"+ {hidden} more open item{(hidden == 1 ? "" : "s")}";
        ExceptionCountText = rows.Count == 1 ? "1 ITEM" : $"{rows.Count} ITEMS";

        var reporting = Stations.Where(s => !s.IsSilent).ToList();
        var done = reporting.Sum(s => s.Snapshot.Completed);

        DoneFooter = reporting.Count == 0
            ? "No station has reported yet. Nothing here is known to be done."
            : $"Everything else — {done} item{(done == 1 ? "" : "s")} across {reporting.Count} "
              + $"station{(reporting.Count == 1 ? "" : "s")} — is done. Completed items only appear in the log.";
    }

    // ---- Actions ----

    private void Act(TechdeskExceptionViewModel exception)
    {
        exception.ActionStatus = RunPage(exception.Station);
    }

    private void Page(TechdeskStationViewModel station)
    {
        station.ActionStatus = RunPage(station);
    }

    /// <summary>
    /// There is no paging channel in the app, and inventing one would be worse than admitting
    /// it: this launches whatever <c>techdeskPage</c> points at — an sms: link, a chat
    /// webhook script — and otherwise says plainly that nothing is configured.
    /// </summary>
    private string RunPage(TechdeskStationViewModel station)
    {
        if (_config.TechdeskPage is not { } configured || string.IsNullOrWhiteSpace(configured.Run))
        {
            return "No paging action configured — set techdeskPage in station.json.";
        }

        var result = _launcher.Launch(new ActionSpec
        {
            Run = configured.Run,
            Args = Substitute(configured.Args, station),
            Also = configured.Also,
        });

        return result.Succeeded
            ? $"Paged at {DateTime.Now:h:mm tt}."
            : $"Could not page: {result.Error}";
    }

    private static string? Substitute(string? args, TechdeskStationViewModel station) =>
        args?.Replace("{station}", station.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("{operator}", station.Snapshot.Operator ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private void MarkNotStaffed(TechdeskStationViewModel station)
    {
        if (!_day.NotStaffed.Contains(station.Key))
        {
            _day.NotStaffed.Add(station.Key);
            _dayStore.Save(_day);
        }

        Sweep();
    }

    [RelayCommand]
    private void Refresh()
    {
        _day = _dayStore.Load(DateOnly.FromDateTime(DateTime.Now));
        Sweep();
    }

    private static DateTime? ParseTime(string? value) =>
        string.IsNullOrWhiteSpace(value) || !TimeOnly.TryParse(value, out var time)
            ? null
            : DateTime.Today.Add(time.ToTimeSpan());

    private static string? LastOctet(string? address)
    {
        var dot = address?.LastIndexOf('.') ?? -1;
        return dot < 0 ? null : address![dot..];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _clockTimer.Stop();
        _sweepTimer.Stop();
        _mapWatch?.Dispose();
    }
}
