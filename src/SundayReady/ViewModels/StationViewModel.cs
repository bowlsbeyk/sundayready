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
    private ServiceSchedule _schedule = new(null);
    private string? _serviceKey;
    private DateTimeOffset? _readyAt;
    private readonly DispatcherTimer _viewerTimer;
    private ViewerCountService? _viewers;
    private bool _polling;
    private bool _pollingViewers;
    private bool _disposed;

    [ObservableProperty]
    private ChecklistTabViewModel? _selectedTab;

    [ObservableProperty]
    private string _clock = string.Empty;

    [ObservableProperty]
    private string _countdown = "—";

    [ObservableProperty]
    private string _countdownLabel = "SERVICE STARTS IN";

    /// <summary>setup / service / postService — see <see cref="StationPhases"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetup), nameof(IsService), nameof(IsPostService),
        nameof(ShowServicePanel), nameof(ShowChecklistArea), nameof(GateLabel), nameof(GateExplanation))]
    private string _phase = StationPhases.Setup;

    /// <summary>Lets an operator look at the list again without leaving the service.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowServicePanel), nameof(ShowChecklistArea))]
    private bool _checklistPinned;

    [ObservableProperty]
    private string _serviceTimer = "0:00";

    [ObservableProperty]
    private string _serviceTimerLabel = "SERVICE STARTS IN";

    [ObservableProperty]
    private string _youTubeViewers = "—";

    [ObservableProperty]
    private string _facebookViewers = "—";

    [ObservableProperty]
    private string _viewersNote = string.Empty;

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

        // The schedule has to exist before the state loads: which service is being prepared
        // for is part of deciding whether the saved ticks still belong to it.
        _schedule = new ServiceSchedule(config.Service);
        _serviceKey = _schedule.Current(DateTime.Now)?.Key;

        _state = stateStore.Load(DateOnly.FromDateTime(DateTime.Now), _serviceKey);
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

        _phase = _state.Phase switch
        {
            StationPhases.Service or StationPhases.PostService => _state.Phase,
            _ => StationPhases.Setup,
        };

        _viewerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _viewerTimer.Tick += (_, _) => _ = PollViewersAsync();

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

    public FirstRunViewModel CreateWalkthrough() => new(_config, _stationLoader, _checklists);

    /// <summary>
    /// The system map, reading the shared maps folder — the same share the techdesk uses, so
    /// every machine sees the same building.
    /// </summary>
    public MapWorkspaceViewModel CreateMapWorkspace() =>
        new(new SystemMapStore(_config.TechdeskShare), _registry);

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

    public string ResetLine => _config.EffectiveResetMode switch
    {
        ResetModes.EveryLaunch => "CLEARS ON LAUNCH",
        ResetModes.PowerCycle => "CLEARS ON POWER CYCLE",
        _ => "RESET 12:01 AM",
    };

    public bool HasDialog => ActiveDialog is not null;

    public ObservableCollection<ChecklistTabViewModel> Tabs { get; } = new();

    public ObservableCollection<QuickLaunchTileViewModel> QuickLaunch { get; } = new();

    public bool HasQuickLaunch => QuickLaunch.Count > 0;

    public bool IsSetup => Phase == StationPhases.Setup;

    public bool IsService => Phase == StationPhases.Service;

    public bool IsPostService => Phase == StationPhases.PostService;

    /// <summary>The big calm panel that replaces the checklist once the station is signed off.</summary>
    public bool ShowServicePanel => IsService && !ChecklistPinned;

    public bool ShowChecklistArea => !ShowServicePanel;

    /// <summary>What has stopped being true since the operator said they were ready.</summary>
    public IEnumerable<ChecklistItemViewModel> FailingNow => AllItems.Where(i => i.IsFailing);

    public bool AnythingFailing => StationFailing > 0;

    public string WatchLine => StationFailing == 0
        ? "Everything that can be checked is still checking out."
        : StationFailing == 1
            ? "One check has stopped passing since you went ready."
            : $"{StationFailing} checks have stopped passing since you went ready.";

    public string SignedOffLine => _state.SignedOffAt is { } at
        ? $"SIGNED OFF {at:h:mm tt}{(_state.OperatorInitials is { Length: > 0 } who ? $" · {who}" : "")}"
        : string.Empty;

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

    /// <summary>
    /// Items on the tabs that gate readiness. A shutdown list is deliberately excluded — it
    /// is done after the service, so counting it would keep the station from ever being ready
    /// before the thing it is preparing for.
    /// </summary>
    public IEnumerable<ChecklistItemViewModel> GatedItems =>
        Tabs.Where(t => t.CountsTowardReady).SelectMany(t => t.Items);

    public int StationTotal => GatedItems.Count();

    public int StationCompleted => GatedItems.Count(i => i.IsChecked);

    public int ItemsLeft => StationTotal - StationCompleted;

    /// <summary>Open only when every item on every tab is checked or overridden. Never per-tab.</summary>
    public bool IsGateOpen => StationTotal > 0 && ItemsLeft == 0;

    public bool IsPartial => GatedItems.Any(i => i.IsOverridden);

    public string GateLabel => Phase switch
    {
        StationPhases.Service => "IN SERVICE",
        StationPhases.PostService => "AFTER THE SERVICE",
        _ => IsGateOpen ? "READY" : "NOT READY YET",
    };

    public string GateExplanation
    {
        get
        {
            if (Phase == StationPhases.Service)
            {
                return "Signed off and under way. The checks are still running — anything that stops passing shows up on the left.";
            }

            if (Phase == StationPhases.PostService)
            {
                return "The service is over. Work through the post-service list; it does not affect readiness.";
            }

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
        _viewerTimer.Start();
        _ = PollViewersAsync();
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
        _schedule = new ServiceSchedule(config.Service);
        ServiceLine = BuildServiceLine(config.Service, _schedule);
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

        // The checklist is done, so it stops being the point. From here the screen watches.
        SetPhase(StationPhases.Service);
        OnPropertyChanged(nameof(SignedOffLine));
    }

    /// <summary>
    /// The operator saying the service is over. Deliberately theirs to press: a service that
    /// runs long or finishes early is normal, and the app has no way to know which.
    /// </summary>
    [RelayCommand]
    private void ServiceFinished()
    {
        SetPhase(StationPhases.PostService);

        // Put the after-the-service work in front of them, which is the point of the change.
        // An explicit choice wins; otherwise fall back to the first list that sits outside the
        // gate, which is nearly always the right guess.
        var after = Tabs.FirstOrDefault(t => t.OpenAfterService)
                    ?? Tabs.FirstOrDefault(t => !t.CountsTowardReady);

        if (after is not null)
        {
            SelectTab(after);
        }
        else
        {
            // Saying nothing was the old behaviour, and it read as the button being broken.
            SetReloadStatus("No checklist is set to open after the service. Tick “Open this "
                + "checklist after the service” on one of them in EDIT.");
        }

        _logger.Log(new LogEntry(StationName, after?.Label ?? StationName, "Service finished",
            LogHow.SignOff, "moved on to the post-service checklist"));
    }

    /// <summary>Back to the checklist during a service, and back out again.</summary>
    [RelayCommand]
    private void ToggleChecklist() => ChecklistPinned = !ChecklistPinned;

    private void SetPhase(string phase)
    {
        Phase = phase;
        ChecklistPinned = false;
        _state.Phase = phase;
        _stateStore.Save(_state);
        UpdateClock();
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

    /// <summary>
    /// Audience figures for the service panel. Telemetry: a failure is an em-dash, never
    /// anything that affects whether the station reads as ready.
    /// </summary>
    private async Task PollViewersAsync()
    {
        if (_pollingViewers || _disposed || !_config.ViewerCounts.Enabled)
        {
            return;
        }

        _pollingViewers = true;
        try
        {
            _viewers ??= new ViewerCountService();
            var counts = await _viewers.ReadAsync(_config.ViewerCounts, _cancellation.Token);

            YouTubeViewers = counts.YouTube?.ToString("N0") ?? "—";
            FacebookViewers = counts.Facebook?.ToString("N0") ?? "—";
            ViewersNote = counts.Note ?? string.Empty;
        }
        catch (Exception)
        {
            YouTubeViewers = "—";
            FacebookViewers = "—";
        }
        finally
        {
            _pollingViewers = false;
        }
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        Clock = now.ToString("h:mm");

        if (_schedule.Current(now) is not { } occurrence)
        {
            Countdown = "—";
            CountdownLabel = "NO SERVICE TIME SET";
            return;
        }

        // The rollover to the next service happens here rather than only at startup, because
        // a PC that is never switched off will never get a startup to do it at.
        if (_serviceKey is not null && !string.Equals(occurrence.Key, _serviceKey, StringComparison.Ordinal))
        {
            RollOverTo(occurrence);
        }

        _serviceKey = occurrence.Key;

        var sinceStart = now - occurrence.Start;
        if (sinceStart >= TimeSpan.Zero)
        {
            ServiceTimerLabel = "INTO THE SERVICE";
            ServiceTimer = sinceStart.TotalHours >= 1
                ? $"{(int)sinceStart.TotalHours}:{sinceStart.Minutes:00}:{sinceStart.Seconds:00}"
                : $"{sinceStart.Minutes:00}:{sinceStart.Seconds:00}";
        }
        else
        {
            var untilStart = occurrence.Start - now;
            ServiceTimerLabel = "SERVICE STARTS IN";
            ServiceTimer = untilStart.TotalHours >= 1
                ? $"{(int)untilStart.TotalHours}:{untilStart.Minutes:00}:{untilStart.Seconds:00}"
                : $"{untilStart.Minutes:00}:{untilStart.Seconds:00}";
        }

        var remaining = occurrence.Start - now;
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

    /// <summary>
    /// Starts the checklist again for the next service. Logged once, not once per item: the
    /// rollover is the event, and twenty CLEARED lines would bury the line explaining them.
    /// </summary>
    private void RollOverTo(ServiceOccurrence occurrence)
    {
        _state.Items.Clear();
        _state.SignedOffAt = null;
        _state.Partial = false;
        _state.ServiceKey = occurrence.Key;

        // Back to setup: the previous service being over does not carry into the next one, and
        // leaving the station "after the service" while it prepares for the next is nonsense.
        _state.Phase = StationPhases.Setup;
        Phase = StationPhases.Setup;
        ChecklistPinned = false;

        _stateStore.Save(_state);

        foreach (var item in AllItems)
        {
            item.ClearForNewService();
        }

        if (SelectedTab is { CountsTowardReady: false } && Tabs.FirstOrDefault(t => t.CountsTowardReady) is { } first)
        {
            SelectTab(first);
        }

        _logger.Log(new LogEntry(
            StationName,
            SelectedTab?.Label ?? StationName,
            $"Preparing for the {occurrence.Display} service",
            LogHow.Cleared,
            "checklist started again for the next service"));

        Refresh();
        _ = PollAsync();
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
        OnPropertyChanged(nameof(AnythingFailing));
        OnPropertyChanged(nameof(WatchLine));
        OnPropertyChanged(nameof(FailingNow));
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

    void IChecklistHost.OpenItemDetail(ChecklistItemViewModel item) =>
        ActiveDialog = new ItemDetailViewModel(item, () => ActiveDialog = null);

    bool IChecklistHost.IsSubStepDone(string key) =>
        _state.Items.TryGetValue(key, out var state) && state.Checked;

    /// <summary>
    /// Sub-steps live in the same per-day store as items, so they clear with everything else
    /// and an operator interrupted halfway through comes back to where they were.
    /// </summary>
    void IChecklistHost.SetSubStepDone(string key, string itemLabel, string subStep, bool done)
    {
        if (done)
        {
            _state.Items[key] = new ItemState
            {
                Checked = true,
                CheckedAt = DateTimeOffset.Now,
                Source = CompletionSources.Manual,
            };
        }
        else
        {
            _state.Items.Remove(key);
        }

        _stateStore.Save(_state);

        // One line per sub-step, indented under its item, so the log reads as the work did.
        _logger.Log(new LogEntry(
            StationName,
            SelectedTab?.Label ?? StationName,
            $"{itemLabel} › {subStep}",
            done ? LogHow.Manual : LogHow.Cleared,
            done ? null : "unchecked"));
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

    /// <summary>
    /// The line under the countdown: every service time, then the venue. With one time this
    /// reads exactly as it did before.
    /// </summary>
    private static string BuildServiceLine(ServiceTimes? service, ServiceSchedule schedule)
    {
        if (service is null && !schedule.HasTimes)
        {
            return string.Empty;
        }

        var parts = new List<string>();

        if (schedule.Describe() is { Length: > 0 } times)
        {
            parts.Add(times);
        }

        if (!string.IsNullOrWhiteSpace(service?.Venue))
        {
            parts.Add(service.Venue.ToUpperInvariant());
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
        _viewerTimer.Stop();
        _viewers?.Dispose();
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
