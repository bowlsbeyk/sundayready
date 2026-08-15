using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SundayReady.Models;
using SundayReady.Services;

namespace SundayReady.ViewModels;

public sealed partial class QuickLaunchTileViewModel : ObservableObject
{
    private readonly ProcessLauncher _launcher;
    private readonly QuickLaunchTile _tile;

    public QuickLaunchTileViewModel(QuickLaunchTile tile, ProcessLauncher launcher)
    {
        _tile = tile;
        _launcher = launcher;
    }

    public string Label => _tile.Label;

    [RelayCommand]
    private void Launch()
    {
        if (_tile.Action is not null)
        {
            _launcher.Launch(_tile.Action);
        }
    }
}

public sealed partial class StationViewModel : ObservableObject, IChecklistHost, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly VerifierRegistry _registry;
    private readonly DailyStateStore _stateStore;
    private readonly CompletionLogger _logger;
    private readonly DailyState _state;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _reloadDebounce;
    private readonly DispatcherTimer _heartbeatTimer;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ProcessLauncher _launcher;

    private SnapshotStore _snapshots;
    private FileSystemWatcher? _watcher;
    private DateTime? _serviceStart;
    private DateTimeOffset? _readyAt;
    private bool _polling;
    private bool _disposed;

    [ObservableProperty]
    private ChecklistTabViewModel? _selectedTab;

    [ObservableProperty]
    private string _clock = string.Empty;

    [ObservableProperty]
    private string _countdown = "—";

    [ObservableProperty]
    private string _countdownLabel = "SERVICE STARTS IN";

    /// <summary>The 1e / override modal currently up, or null. Rendered as an overlay.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDialog))]
    private object? _activeDialog;

    [ObservableProperty]
    private string _stationName = "SundayReady";

    [ObservableProperty]
    private string _serviceLine = string.Empty;

    [ObservableProperty]
    private string _operatorLine = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    private string? _loadError;

    /// <summary>Confirms a reload happened, so an author saving a file gets an answer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReloadStatus))]
    private string? _reloadStatus;

    private StationConfig _config;
    private readonly ChecklistLoader _checklists;
    private readonly StationConfigLoader _stationLoader;

    public StationViewModel(
        StationConfig config,
        IReadOnlyList<ChecklistDefinition> definitions,
        VerifierRegistry registry,
        ProcessLauncher launcher,
        DailyStateStore stateStore,
        CompletionLogger logger,
        ChecklistLoader checklists,
        StationConfigLoader stationLoader,
        string? loadError = null)
    {
        _registry = registry;
        _stateStore = stateStore;
        _logger = logger;
        _config = config;
        _checklists = checklists;
        _stationLoader = stationLoader;
        _launcher = launcher;

        HostLine = $"HOST {Environment.MachineName.ToUpperInvariant()} · "
            + (stationLoader.FileExists ? "station.json" : "hostname auto-detect");

        _state = stateStore.Load(DateOnly.FromDateTime(DateTime.Now));
        _logger.OperatorInitials = _state.OperatorInitials;
        _snapshots = new SnapshotStore(config.TechdeskShare);

        ApplyConfig(config);
        BuildTabs(definitions);
        LoadError = loadError;

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();

        _pollTimer = new DispatcherTimer { Interval = PollInterval };
        _pollTimer.Tick += (_, _) => _ = PollAsync();

        // Editors save in bursts — truncate, write, touch — so wait for the dust to settle.
        _reloadDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _reloadDebounce.Tick += (_, _) =>
        {
            _reloadDebounce.Stop();
            Reload(automatic: true);
        };

        _heartbeatTimer = new DispatcherTimer { Interval = SnapshotStore.PublishInterval };
        _heartbeatTimer.Tick += (_, _) => Publish();

        UpdateClock();
        Refresh();
    }

    public string HostLine { get; }

    public bool HasLoadError => !string.IsNullOrEmpty(LoadError);

    public bool HasReloadStatus => !string.IsNullOrEmpty(ReloadStatus);

    /// <summary>A freshly installed station with nothing set up yet. Not an error state.</summary>
    public bool HasNoChecklists => Tabs.Count == 0 && !HasLoadError;

    public string ChecklistsFolder => _checklists.Directory;

    public string Version => AppVersion.Display;

    /// <summary>
    /// Settings and the completion log are separate windows in the design. The station owns
    /// their view models so they see the same config and the same day's state.
    /// </summary>
    public SettingsViewModel CreateSettings() => new(_config, _checklists, _stationLoader, _registry);

    public CompletionLogViewModel CreateLog() => new(this);

    /// <summary>
    /// The techdesk as a window from a station, so you can look at it without flipping this
    /// PC into techdesk mode and restarting. The mode setting still exists for the PC that
    /// really is the techdesk and should boot straight into it.
    /// </summary>
    public TechdeskViewModel CreateTechdesk() => new(_config, _launcher);

    public ChecklistEditorViewModel CreateEditor() =>
        new(_config, _checklists, new ChecklistWriter(_checklists.Directory), _stationLoader, _registry);

    public string DateLine => DateTime.Now.ToString("ddd dd MMM yyyy").ToUpperInvariant();

    public string ResetLine => _config.ResetOnRestart ? "CLEARS ON RESTART" : "RESET 12:01 AM";

    public bool HasDialog => ActiveDialog is not null;

    public ObservableCollection<ChecklistTabViewModel> Tabs { get; } = new();

    public ObservableCollection<QuickLaunchTileViewModel> QuickLaunch { get; } = new();

    public bool HasQuickLaunch => QuickLaunch.Count > 0;

    public IEnumerable<ChecklistItemViewModel> AllItems => Tabs.SelectMany(t => t.Items);

    // ---- Ring. Reflects the selected tab; the gate is what spans every tab. ----

    public int TabCompleted => SelectedTab?.CompletedCount ?? 0;

    public int TabTotal => SelectedTab?.TotalCount ?? 0;

    public int TabFailing => SelectedTab?.FailingCount ?? 0;

    public double CompletedFraction => TabTotal == 0 ? 0 : (double)TabCompleted / TabTotal;

    public double FailingFraction => TabTotal == 0 ? 0 : (double)TabFailing / TabTotal;

    /// <summary>The completed arc goes amber while anything on the tab is failing.</summary>
    public bool RingIsHealthy => TabFailing == 0;

    public string PercentText => TabTotal == 0 ? "0" : Math.Round(CompletedFraction * 100).ToString("0");

    public string RingCaption => TabFailing > 0
        ? $"{TabCompleted} OF {TabTotal} · {TabFailing} FAILING"
        : $"{TabCompleted} OF {TabTotal} DONE";

    // ---- Top-bar status pill ----

    public int StationFailing => AllItems.Count(i => i.IsFailing);

    public bool PillIsFailing => StationFailing > 0;

    public string PillText
    {
        get
        {
            if (StationFailing > 0)
            {
                return StationFailing == 1 ? "1 VERIFIER FAILING" : $"{StationFailing} VERIFIERS FAILING";
            }

            var internet = AllItems.FirstOrDefault(i =>
                string.Equals(i.Item.Verify?.Kind, "internetReachable", StringComparison.OrdinalIgnoreCase));

            if (internet is { Status: VerifyStatus.Passed })
            {
                return $"INTERNET OK · {internet.LastResult}";
            }

            return AllItems.Any(i => i.HasVerify) ? "NO VERIFIERS FAILING" : "NO AUTOMATIC CHECKS";
        }
    }

    // ---- The gate ----

    public int StationTotal => AllItems.Count();

    public int StationCompleted => AllItems.Count(i => i.IsChecked);

    public int ItemsLeft => StationTotal - StationCompleted;

    /// <summary>Open only when every item on every tab is checked or overridden. Never per-tab.</summary>
    public bool IsGateOpen => StationTotal > 0 && ItemsLeft == 0;

    public bool IsPartial => AllItems.Any(i => i.IsOverridden);

    public string GateLabel => IsGateOpen ? "READY" : "NOT READY YET";

    public string GateExplanation
    {
        get
        {
            if (StationTotal == 0)
            {
                return "Nothing to check yet. Build a checklist and this unlocks once every item on it is done.";
            }

            if (!IsGateOpen)
            {
                return $"{ItemsLeft} item{(ItemsLeft == 1 ? "" : "s")} left. The Ready to go button unlocks when every item on every tab is checked.";
            }

            return IsPartial
                ? "Every item is accounted for, but some were overridden — this service is recorded as partial."
                : "Every item on every tab is checked. Have a good service.";
        }
    }

    public bool ShowFailureAdvisory => StationFailing > 0;

    public string FailureAdvisory =>
        "Fix or override the failing item to unlock Ready to go. Overrides are written to the completion log with your initials.";

    public CompletionLogger Logger => _logger;

    public int OverriddenCount => AllItems.Count(i => i.IsOverridden);

    public string? OperatorInitials => _state.OperatorInitials;

    public DateTimeOffset? SignedOffAt => _state.SignedOffAt;

    /// <summary>
    /// The operator declaring themselves ready, from the completion log screen. Unlike the
    /// gate this is allowed with items still open — it just records the service as partial,
    /// which is the honest outcome rather than a blocked button at five to eleven.
    /// </summary>
    public void SignOff(string initials, string? notes)
    {
        _state.OperatorInitials = initials;
        _state.SignedOffAt = DateTimeOffset.Now;
        _state.Partial = ItemsLeft > 0 || OverriddenCount > 0;
        _logger.OperatorInitials = initials;
        _stateStore.Save(_state);

        var detail = _state.Partial
            ? $"partial — {ItemsLeft} open, {OverriddenCount} overridden"
            : "all items verified";

        if (!string.IsNullOrWhiteSpace(notes))
        {
            detail = $"{detail}. {notes.Trim()}";
        }

        _logger.Log(new LogEntry(StationName, SelectedTab?.Label ?? StationName, "Sign off", LogHow.SignOff, detail, initials));

        OnPropertyChanged(nameof(OperatorInitials));
        OnPropertyChanged(nameof(SignedOffAt));
    }

    public void Start()
    {
        _clockTimer.Start();
        _pollTimer.Start();
        _heartbeatTimer.Start();
        StartWatching();
        Publish();
        _ = PollAsync();
    }

    /// <summary>
    /// Writes this station's heartbeat to the techdesk share. Every station does this whether
    /// or not a techdesk is running — a station has no way to know, and a snapshot nobody
    /// reads costs a few kilobytes a minute.
    /// </summary>
    private void Publish()
    {
        if (_disposed)
        {
            return;
        }

        _snapshots.Publish(new StationSnapshot
        {
            Station = StationName,
            Host = Environment.MachineName.ToUpperInvariant(),
            Address = SnapshotStore.Address,
            Completed = StationCompleted,
            Total = StationTotal,
            Percentage = StationTotal == 0 ? 0 : (int)Math.Round(100.0 * StationCompleted / StationTotal),
            Failing = StationFailing,
            Operator = _config.Operator,
            ReadyAt = IsGateOpen ? _readyAt : null,
            LastHeartbeat = DateTimeOffset.Now,
            PingMs = MeasuredPing(),
            Items = AllItems.Select(i => i.ToSnapshotItem()).ToList(),
        });
    }

    /// <summary>
    /// The one network figure the app actually measures: the round trip this station's
    /// <c>internetReachable</c> verifier last reported. Nothing else on the techdesk's
    /// telemetry rail is real, and nothing else is invented to fill it.
    /// </summary>
    private int? MeasuredPing()
    {
        var internet = AllItems.FirstOrDefault(i =>
            string.Equals(i.Item.Verify?.Kind, "internetReachable", StringComparison.OrdinalIgnoreCase)
            && i.Status == VerifyStatus.Passed);

        if (internet?.LastResult is not { } result)
        {
            return null;
        }

        var digits = new string(result.TakeWhile(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, out var milliseconds) ? milliseconds : null;
    }

    /// <summary>
    /// Watches for edits to the checklist files and station.json. Authoring a checklist means
    /// saving a file and looking at the result, so a restart between every edit is the wrong
    /// loop. Both live under the exe's folder, so one watcher covers them.
    /// </summary>
    private void StartWatching()
    {
        try
        {
            var root = Path.GetDirectoryName(_stationLoader.FilePath);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return;
            }

            _watcher = new FileSystemWatcher(root, "*.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true,
            };

            _watcher.Changed += OnWatchedFileChanged;
            _watcher.Created += OnWatchedFileChanged;
            _watcher.Deleted += OnWatchedFileChanged;
            _watcher.Renamed += OnWatchedFileChanged;
        }
        catch (Exception)
        {
            // No watcher is survivable — the reload button still works.
        }
    }

    private void OnWatchedFileChanged(object? sender, FileSystemEventArgs e)
    {
        // Watcher callbacks arrive on a pool thread; everything downstream touches the UI.
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            _reloadDebounce.Stop();
            _reloadDebounce.Start();
        });
    }

    [RelayCommand]
    private void Reload() => Reload(automatic: false);

    private void Reload(bool automatic)
    {
        StationConfig config;
        try
        {
            config = _stationLoader.Load();
        }
        catch (Exception)
        {
            config = _config;
        }

        var definitions = new List<ChecklistDefinition>();
        var errors = new List<string>();

        foreach (var file in config.Checklists)
        {
            try
            {
                definitions.Add(_checklists.Load(file));
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

        // Nothing usable came back but we already have tabs on screen: almost always a file
        // caught mid-save. Report it and keep what the operator is looking at.
        if (definitions.Count == 0 && Tabs.Count > 0)
        {
            LoadError = string.Join(Environment.NewLine, errors);
            SetReloadStatus(null);
            return;
        }

        ApplyConfig(config);
        BuildTabs(definitions);
        LoadError = errors.Count == 0 ? null : string.Join(Environment.NewLine, errors);

        SetReloadStatus(errors.Count > 0
            ? null
            : $"{(automatic ? "Reloaded" : "Reloaded by hand")} · {definitions.Count} checklist"
              + $"{(definitions.Count == 1 ? "" : "s")}, {StationTotal} items · {DateTime.Now:HH:mm:ss}");

        Refresh();
        _ = PollAsync();
    }

    private void SetReloadStatus(string? message)
    {
        ReloadStatus = message;

        if (message is null)
        {
            return;
        }

        // Confirmation, not a permanent fixture.
        var clear = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        clear.Tick += (s, _) =>
        {
            ((DispatcherTimer)s!).Stop();
            if (ReloadStatus == message)
            {
                ReloadStatus = null;
            }
        };
        clear.Start();
    }

    private void ApplyConfig(StationConfig config)
    {
        _config = config;

        if (!string.Equals(_snapshots.Directory, SnapshotStore.Resolve(config.TechdeskShare), StringComparison.OrdinalIgnoreCase))
        {
            _snapshots = new SnapshotStore(config.TechdeskShare);
        }

        StationName = string.IsNullOrWhiteSpace(config.Station) ? "SundayReady" : config.Station;
        _serviceStart = ParseServiceTime(config.Service?.StartsAt);
        ServiceLine = BuildServiceLine(config.Service);
        OperatorLine = string.IsNullOrWhiteSpace(config.Operator)
            ? "OPERATOR — NOT SET"
            : $"OPERATOR — {config.Operator.ToUpperInvariant()}";

        QuickLaunch.Clear();
        foreach (var tile in config.QuickLaunch)
        {
            QuickLaunch.Add(new QuickLaunchTileViewModel(tile, _launcher));
        }

        OnPropertyChanged(nameof(HasQuickLaunch));
        UpdateClock();
    }

    /// <summary>
    /// Rebuilds the tabs, carrying poll progress and the selected tab across. Checked state
    /// needs no carrying — it is keyed by file and label in the day's saved state.
    /// </summary>
    private void BuildTabs(IReadOnlyList<ChecklistDefinition> definitions)
    {
        var previous = new Dictionary<string, ChecklistItemViewModel>();
        foreach (var item in AllItems)
        {
            previous[item.StateKey] = item;
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        var selectedLabel = SelectedTab?.Label;
        Tabs.Clear();

        foreach (var definition in definitions)
        {
            var items = definition.Items.Select(item =>
            {
                IVerifier? verifier = null;
                if (item.Verify is not null)
                {
                    _registry.TryGet(item.Verify, out verifier!);
                }

                _state.Items.TryGetValue(DailyStateStore.KeyFor(definition.SourceFile, item.Label), out var restored);
                var viewModel = new ChecklistItemViewModel(item, definition, _launcher, this, verifier, restored);

                if (previous.TryGetValue(viewModel.StateKey, out var older))
                {
                    viewModel.AdoptRuntimeStateFrom(older);
                }

                return viewModel;
            }).ToList();

            foreach (var item in items)
            {
                item.PropertyChanged += OnItemPropertyChanged;
            }

            Tabs.Add(new ChecklistTabViewModel(definition, items) { Selecting = SelectTab });
        }

        var selected = Tabs.FirstOrDefault(t => t.Label == selectedLabel) ?? Tabs.FirstOrDefault();
        foreach (var tab in Tabs)
        {
            tab.IsSelected = ReferenceEquals(tab, selected);
        }

        SelectedTab = selected;
    }

    private void SelectTab(ChecklistTabViewModel tab)
    {
        foreach (var candidate in Tabs)
        {
            candidate.IsSelected = ReferenceEquals(candidate, tab);
        }

        SelectedTab = tab;
        RefreshRing();
    }

    [RelayCommand]
    private void ReadyToGo()
    {
        if (!IsGateOpen)
        {
            return;
        }

        _state.SignedOffAt = DateTimeOffset.Now;
        _state.Partial = IsPartial;
        _stateStore.Save(_state);

        _logger.Log(new LogEntry(
            StationName,
            SelectedTab?.Label ?? StationName,
            "Ready to go",
            LogHow.SignOff,
            IsPartial ? "signed off with overrides — service marked partial" : "all items verified"));

        OnPropertyChanged(nameof(GateLabel));
        OnPropertyChanged(nameof(GateExplanation));
    }

    private async Task PollAsync()
    {
        // The timer keeps firing while a slow HTTP check is in flight; skip rather than pile up.
        if (_polling || _disposed)
        {
            return;
        }

        _polling = true;
        try
        {
            foreach (var item in AllItems.Where(i => i.HasVerify).ToList())
            {
                if (_cancellation.IsCancellationRequested)
                {
                    return;
                }

                await item.PollAsync(_cancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            _polling = false;
            Refresh();
        }
    }

    private void UpdateClock()
    {
        Clock = DateTime.Now.ToString("h:mm");

        if (_serviceStart is not { } start)
        {
            Countdown = "—";
            CountdownLabel = "NO SERVICE TIME SET";
            return;
        }

        var remaining = start - DateTime.Now;
        if (remaining <= TimeSpan.Zero)
        {
            Countdown = "NOW";
            CountdownLabel = "SERVICE HAS STARTED";
            return;
        }

        CountdownLabel = "SERVICE STARTS IN";

        // Anything over an hour formats H:MM:SS — a three-digit minute count at 46px is
        // unreadable and collides with its label.
        Countdown = remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : $"{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChecklistItemViewModel.IsChecked) or nameof(ChecklistItemViewModel.Status))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        foreach (var tab in Tabs)
        {
            tab.Refresh();
        }

        // The moment the gate opened, which is what the techdesk stamps as READY 10:06 AM.
        // Taken here rather than in ReadyToGo: the gate opens when the last item ticks, and
        // nobody has to press the button for that to be the truth.
        _readyAt = IsGateOpen ? _readyAt ?? _state.SignedOffAt ?? DateTimeOffset.Now : null;

        RefreshRing();

        OnPropertyChanged(nameof(StationFailing));
        OnPropertyChanged(nameof(PillIsFailing));
        OnPropertyChanged(nameof(PillText));
        OnPropertyChanged(nameof(StationCompleted));
        OnPropertyChanged(nameof(ItemsLeft));
        OnPropertyChanged(nameof(IsGateOpen));
        OnPropertyChanged(nameof(IsPartial));
        OnPropertyChanged(nameof(GateLabel));
        OnPropertyChanged(nameof(GateExplanation));
        OnPropertyChanged(nameof(ShowFailureAdvisory));
        OnPropertyChanged(nameof(HasNoChecklists));
    }

    private void RefreshRing()
    {
        OnPropertyChanged(nameof(TabCompleted));
        OnPropertyChanged(nameof(TabTotal));
        OnPropertyChanged(nameof(TabFailing));
        OnPropertyChanged(nameof(CompletedFraction));
        OnPropertyChanged(nameof(FailingFraction));
        OnPropertyChanged(nameof(RingIsHealthy));
        OnPropertyChanged(nameof(PercentText));
        OnPropertyChanged(nameof(RingCaption));
    }

    // ---- IChecklistHost ----

    void IChecklistHost.ItemChanged(ChecklistItemViewModel item, string how, string? detail, TimeSpan? duration)
    {
        if (item.IsChecked)
        {
            _state.Items[item.StateKey] = new ItemState
            {
                Checked = true,
                CheckedBy = item.CheckedBy,
                CheckedAt = item.CheckedAt,
                Source = item.CompletionSource,
                OverrideNote = item.OverrideNote,
            };
        }
        else
        {
            _state.Items.Remove(item.StateKey);
        }

        _stateStore.Save(_state);

        // An empty verb means the caller logs this transition itself; persist only.
        if (!string.IsNullOrEmpty(how))
        {
            _logger.Log(new LogEntry(StationName, item.TabLabel, item.Label, how, detail, item.CheckedBy, duration));
        }

        Refresh();
    }

    void IChecklistHost.OpenFailedDetail(ChecklistItemViewModel item)
    {
        var number = item.Source.Items.IndexOf(item.Item) + 1;
        ActiveDialog = new FailedVerifyViewModel(item, number, () => ActiveDialog = null);
    }

    void IChecklistHost.OpenOverride(ChecklistItemViewModel item)
    {
        ActiveDialog = new OverrideViewModel(
            item,
            _state.OperatorInitials,
            (initials, note) =>
            {
                _state.OperatorInitials = initials;
                _logger.OperatorInitials = initials;
                item.ApplyOverride(initials, note);
            },
            () => ActiveDialog = null);
    }

    private static DateTime? ParseServiceTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !TimeOnly.TryParse(value, out var time))
        {
            return null;
        }

        return DateTime.Today.Add(time.ToTimeSpan());
    }

    private static string BuildServiceLine(ServiceTimes? service)
    {
        if (service is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(service.StartsAt) && TimeOnly.TryParse(service.StartsAt, out var start))
        {
            parts.Add(start.ToString("h:mm tt").ToUpperInvariant());
        }

        if (!string.IsNullOrWhiteSpace(service.Venue))
        {
            parts.Add(service.Venue.ToUpperInvariant());
        }
        else if (!string.IsNullOrWhiteSpace(service.StreamAt) && TimeOnly.TryParse(service.StreamAt, out var stream))
        {
            parts.Add($"STREAM {stream:h:mm}");
        }

        return string.Join(" · ", parts);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _clockTimer.Stop();
        _pollTimer.Stop();
        _reloadDebounce.Stop();
        _heartbeatTimer.Stop();

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnWatchedFileChanged;
            _watcher.Created -= OnWatchedFileChanged;
            _watcher.Deleted -= OnWatchedFileChanged;
            _watcher.Renamed -= OnWatchedFileChanged;
            _watcher.Dispose();
        }

        foreach (var item in AllItems)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        _cancellation.Cancel();
        _cancellation.Dispose();
        _registry.Dispose();
    }
}
