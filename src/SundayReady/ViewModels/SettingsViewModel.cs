using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SundayReady.Models;
using SundayReady.Services;

namespace SundayReady.ViewModels;

public sealed partial class SettingsPageViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public SettingsPageViewModel(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }

    public string Label { get; }

    public Action<SettingsPageViewModel>? Selecting { get; set; }

    [RelayCommand]
    private void Select() => Selecting?.Invoke(this);
}

/// <summary>One row of CHECKLISTS LOADED.</summary>
public sealed class ChecklistFileViewModel
{
    public ChecklistFileViewModel(string fileName, ChecklistDefinition? definition, int unknownKinds, DateTime? edited, string? error)
    {
        FileName = fileName;
        Error = error;
        UnknownKinds = unknownKinds;

        var count = definition?.Items.Count ?? 0;
        Detail = error is not null
            ? "WILL NOT PARSE"
            : unknownKinds > 0
                ? $"{count} items · {unknownKinds} UNKNOWN VERIFIER KIND"
                : $"{count} items · tab \"{definition?.TabLabel}\"";

        Edited = edited?.ToString("d MMM HH:mm").ToUpperInvariant() ?? "";
    }

    public string FileName { get; }

    public string Detail { get; }

    public string Edited { get; }

    public int UnknownKinds { get; }

    public string? Error { get; }

    public bool IsHealthy => Error is null && UnknownKinds == 0;

    public bool IsWarning => Error is null && UnknownKinds > 0;

    public bool IsBroken => Error is not null;
}

/// <summary>One tile of VERIFIER HEALTH.</summary>
public sealed class VerifierTileViewModel
{
    public VerifierTileViewModel(string kind, bool isStub, int usageCount)
    {
        Kind = kind;
        IsStub = isStub;
        Status = isStub
            ? "STUB · TODO"
            : usageCount == 0 ? "OK · unused" : $"OK · {usageCount} item{(usageCount == 1 ? "" : "s")}";
    }

    public string Kind { get; }

    public string Status { get; }

    public bool IsStub { get; }
}

/// <summary>
/// Points this PC at the right station identity and checklist files, and shows whether the
/// verifiers behind them are healthy. Everything here is local to this machine.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ChecklistLoader _checklists;
    private readonly StationConfigLoader _stationLoader;
    private readonly VerifierRegistry _registry;
    private readonly UpdateService _updates;

    [ObservableProperty]
    private SettingsPageViewModel? _selectedPage;

    [ObservableProperty]
    private string _stationName = string.Empty;

    [ObservableProperty]
    private string _operator = string.Empty;

    [ObservableProperty]
    private bool _techdesk;

    [ObservableProperty]
    private string _doorsAt = string.Empty;

    [ObservableProperty]
    private string _streamAt = string.Empty;

    [ObservableProperty]
    private string _startsAt = string.Empty;

    [ObservableProperty]
    private string _venue = string.Empty;

    [ObservableProperty]
    private bool _updatesEnabled = true;

    [ObservableProperty]
    private string _updateStatus = string.Empty;

    [ObservableProperty]
    private bool _isCheckingUpdate;

    [ObservableProperty]
    private string _saveStatus = string.Empty;

    public SettingsViewModel(
        StationConfig config,
        ChecklistLoader checklists,
        StationConfigLoader stationLoader,
        VerifierRegistry registry)
    {
        Config = config;
        _checklists = checklists;
        _stationLoader = stationLoader;
        _registry = registry;
        _updates = new UpdateService(config.Updates.Repository);

        foreach (var (key, label) in new[]
                 {
                     ("identity", "Identity"),
                     ("checklists", "Checklists"),
                     ("verifiers", "Verifiers"),
                     ("service", "Service times"),
                     ("logging", "Logging"),
                     ("updates", "Updates"),
                     ("techdesk", "Techdesk mode"),
                 })
        {
            Pages.Add(new SettingsPageViewModel(key, label) { Selecting = SelectPage });
        }

        SelectedPage = Pages[0];
        SelectedPage.IsSelected = true;

        LoadFromConfig();
        RefreshInspection();
        UpdateStatus = DescribePending();
    }

    public StationConfig Config { get; }

    public ObservableCollection<SettingsPageViewModel> Pages { get; } = new();

    public ObservableCollection<ChecklistFileViewModel> Files { get; } = new();

    public ObservableCollection<VerifierTileViewModel> Verifiers { get; } = new();

    public string Hostname => _stationLoader.DetectedHostname;

    public string ConfigPath => _stationLoader.FilePath;

    public string ChecklistsNote => $"FROM {_checklists.Directory}";

    public string LogsDirectory => AppPaths.LogsDirectory;

    public string DataDirectory => AppPaths.DataDirectory;

    public string PollIntervalTile => "POLL INTERVAL 5 s";

    public string CurrentVersion => AppVersion.Display;

    public string Repository => _updates.Repository;

    public string ReleasesUrl => _updates.ReleasesUrl;

    public bool ShowIdentity => SelectedPage?.Key == "identity";

    public bool ShowChecklists => SelectedPage?.Key == "checklists";

    public bool ShowVerifiers => SelectedPage?.Key == "verifiers";

    public bool ShowService => SelectedPage?.Key == "service";

    public bool ShowLogging => SelectedPage?.Key == "logging";

    public bool ShowUpdates => SelectedPage?.Key == "updates";

    public bool ShowTechdesk => SelectedPage?.Key == "techdesk";

    private void SelectPage(SettingsPageViewModel page)
    {
        foreach (var candidate in Pages)
        {
            candidate.IsSelected = ReferenceEquals(candidate, page);
        }

        SelectedPage = page;

        OnPropertyChanged(nameof(ShowIdentity));
        OnPropertyChanged(nameof(ShowChecklists));
        OnPropertyChanged(nameof(ShowVerifiers));
        OnPropertyChanged(nameof(ShowService));
        OnPropertyChanged(nameof(ShowLogging));
        OnPropertyChanged(nameof(ShowUpdates));
        OnPropertyChanged(nameof(ShowTechdesk));
    }

    private void LoadFromConfig()
    {
        StationName = Config.Station;
        Operator = Config.Operator ?? string.Empty;
        Techdesk = Config.Techdesk;
        DoorsAt = Config.Service?.DoorsAt ?? string.Empty;
        StreamAt = Config.Service?.StreamAt ?? string.Empty;
        StartsAt = Config.Service?.StartsAt ?? string.Empty;
        Venue = Config.Service?.Venue ?? string.Empty;
        UpdatesEnabled = Config.Updates.Enabled;
    }

    /// <summary>
    /// Re-reads every checklist file so the list reflects what is actually on disk, including
    /// files this station does not load. A file with an unrecognised verifier kind is reported
    /// and still counted as loadable — one bad item never disqualifies a file.
    /// </summary>
    private void RefreshInspection()
    {
        Files.Clear();
        var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in _checklists.ListFiles())
        {
            var path = Path.Combine(_checklists.Directory, file);
            var edited = File.Exists(path) ? File.GetLastWriteTime(path) : (DateTime?)null;

            try
            {
                var definition = _checklists.Load(file);
                var unknown = 0;

                foreach (var kind in definition.Items.Select(i => i.Verify?.Kind).Where(k => k is not null))
                {
                    if (_registry.Knows(kind))
                    {
                        usage[kind!] = usage.GetValueOrDefault(kind!) + 1;
                    }
                    else
                    {
                        unknown++;
                    }
                }

                Files.Add(new ChecklistFileViewModel(file, definition, unknown, edited, null));
            }
            catch (Exception ex)
            {
                Files.Add(new ChecklistFileViewModel(file, null, 0, edited, ex.Message));
            }
        }

        Verifiers.Clear();
        foreach (var verifier in _registry.All.OrderBy(v => v.Kind, StringComparer.OrdinalIgnoreCase))
        {
            Verifiers.Add(new VerifierTileViewModel(verifier.Kind, verifier.IsStub, usage.GetValueOrDefault(verifier.Kind)));
        }
    }

    private static string DescribePending()
    {
        var pending = UpdateService.ReadPending();
        if (pending is null)
        {
            return $"Running {AppVersion.Display}. No update staged.";
        }

        return pending.FailedAttempts > 0
            ? $"{pending.Version} is staged but could not be applied: {pending.LastError}"
            : $"{pending.Version} is staged. It installs the next time this PC starts SundayReady.";
    }

    [RelayCommand]
    private void Save()
    {
        Config.Station = StationName.Trim();
        Config.Operator = string.IsNullOrWhiteSpace(Operator) ? null : Operator.Trim();
        Config.Techdesk = Techdesk;
        Config.Updates.Enabled = UpdatesEnabled;
        Config.Service = new ServiceTimes
        {
            DoorsAt = Blank(DoorsAt),
            StreamAt = Blank(StreamAt),
            StartsAt = Blank(StartsAt),
            Venue = Blank(Venue),
        };

        try
        {
            _stationLoader.Save(Config);
            SaveStatus = $"Saved to {ConfigPath}. Restart SundayReady to pick it up.";
        }
        catch (Exception ex)
        {
            SaveStatus = $"Could not save: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Reload()
    {
        LoadFromConfig();
        RefreshInspection();
        SaveStatus = "Reloaded from disk. Unsaved changes were discarded.";
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        IsCheckingUpdate = true;
        UpdateStatus = $"Checking {Repository}…";

        try
        {
            var available = await _updates.CheckAsync(CancellationToken.None);
            if (available is null)
            {
                UpdateStatus = $"{AppVersion.Display} is the latest release.";
                return;
            }

            UpdateStatus = $"Downloading {available.Tag}…";
            await _updates.StageAsync(available, CancellationToken.None);
            UpdateStatus = $"{available.Tag} downloaded. It installs the next time this PC starts SundayReady.";
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Could not check for updates: {ex.Message}";
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private void OpenChecklistsFolder() => OpenPath(_checklists.Directory);

    [RelayCommand]
    private void OpenLogsFolder() => OpenPath(AppPaths.LogsDirectory);

    [RelayCommand]
    private void OpenReleases() => OpenPath(ReleasesUrl);

    private static void OpenPath(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            })?.Dispose();
        }
        catch (Exception)
        {
            // Nothing worth interrupting the operator over.
        }
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
