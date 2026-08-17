using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SundayReady.Models;
using SundayReady.Services;

namespace SundayReady.ViewModels;

/// <summary>One checklist file already on disk, and whether this station shows it as a tab.</summary>
public sealed partial class SetupChecklistViewModel : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty]
    private bool _isSelected;

    public SetupChecklistViewModel(string fileName, ChecklistDefinition? definition, Action changed)
    {
        _changed = changed;
        FileName = fileName;
        TabLabel = definition?.TabLabel ?? fileName;
        Station = definition?.Station ?? string.Empty;
        ItemCount = definition?.Items.Count ?? 0;
        IsReadable = definition is not null;
        AfterService = definition is { CountsTowardReady: false };
    }

    public string FileName { get; }

    public string TabLabel { get; }

    public string Station { get; }

    public int ItemCount { get; }

    public bool IsReadable { get; }

    public bool AfterService { get; }

    /// <summary>"12 items · for Livestream Video", or why it cannot be used.</summary>
    public string Detail => !IsReadable
        ? "This file will not parse — it is skipped."
        : $"{ItemCount} item{(ItemCount == 1 ? "" : "s")}"
            + (string.IsNullOrWhiteSpace(Station) ? "" : $" · written for {Station}")
            + (AfterService ? " · sits outside the Ready gate" : "");

    [RelayCommand]
    private void Toggle()
    {
        if (IsReadable)
        {
            IsSelected = !IsSelected;
        }
    }

    partial void OnIsSelectedChanged(bool value) => _changed();
}

/// <summary>A starting point on the checklist step.</summary>
public sealed partial class SetupTemplateViewModel : ObservableObject
{
    private readonly Action<SetupTemplateViewModel> _selecting;

    [ObservableProperty]
    private bool _isSelected;

    public SetupTemplateViewModel(ChecklistTemplate template, Action<SetupTemplateViewModel> selecting)
    {
        Template = template;
        _selecting = selecting;
    }

    public ChecklistTemplate Template { get; }

    public string Title => Template.Title;

    public string Summary => Template.Summary;

    public string Detail => Template.Key == "blank"
        ? Template.Summary
        : $"{Template.Summary} {Template.ItemCount} items to start from.";

    [RelayCommand]
    private void Select() => _selecting(this);
}

/// <summary>
/// The first-time setup walkthrough: six screens that end with a station which actually works.
/// <para>
/// It exists because the honest alternative was what a new user used to get — an empty app and a
/// dashed box telling them to press EDIT and then SETTINGS. That tells someone what to do without
/// helping them do it, and it assumes they already know what a station, a tab and a verifier are.
/// </para>
/// <para>
/// So each step explains one idea in plain words and then does the thing, and the walkthrough
/// writes real <c>station.json</c> and real checklist files — nothing here is a special mode. It
/// can be skipped from any step, and re-run later from Settings, because trapping someone in a
/// wizard is its own kind of unfriendly.
/// </para>
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    /// <summary>Kept in sync with the step titles below; index into it is <see cref="Step"/>.</summary>
    private static readonly string[] StepTitles =
    {
        "Welcome",
        "This station",
        "Service times",
        "The checklist",
        "Finishing touches",
        "Ready",
    };

    private const int LastStep = 5;

    private readonly StationConfig _config;
    private readonly StationConfigLoader _stationLoader;
    private readonly ChecklistLoader _checklists;
    private readonly ChecklistWriter _writer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepTitle), nameof(StepNumberLabel), nameof(IsWelcome),
        nameof(IsStation), nameof(IsServices), nameof(IsChecklists), nameof(IsTouches), nameof(IsDone),
        nameof(CanGoBack), nameof(IsLastStep), nameof(NextLabel))]
    private int _step;

    [ObservableProperty]
    private string _stationName = string.Empty;

    [ObservableProperty]
    private string _operatorName = string.Empty;

    [ObservableProperty]
    private string _serviceTimes = string.Empty;

    [ObservableProperty]
    private int _resetLeadMinutes = ServiceSchedule.DefaultLeadMinutes;

    [ObservableProperty]
    private bool _resetEveryLaunch = true;

    [ObservableProperty]
    private bool _resetPowerCycle;

    [ObservableProperty]
    private bool _resetDaily;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewChecklistFileName))]
    private string _newChecklistName = string.Empty;

    [ObservableProperty]
    private bool _startAtLogon;

    [ObservableProperty]
    private string _logonStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFinishError))]
    private string? _finishError;

    [ObservableProperty]
    private string _summary = string.Empty;

    public FirstRunViewModel(
        StationConfig config,
        StationConfigLoader stationLoader,
        ChecklistLoader checklists)
    {
        _config = config;
        _stationLoader = stationLoader;
        _checklists = checklists;
        _writer = new ChecklistWriter(checklists.Directory);

        Hostname = stationLoader.DetectedHostname;
        StationName = string.IsNullOrWhiteSpace(config.Station) ? Hostname : config.Station;
        OperatorName = config.Operator ?? string.Empty;

        var starts = config.Service?.Starts.Count > 0
            ? config.Service.Starts
            : new List<string>();
        ServiceTimes = starts.Count > 0 ? string.Join(Environment.NewLine, starts) : "10:30";

        if (config.Service is { } service)
        {
            ResetLeadMinutes = service.ResetLeadMinutes;
        }

        ApplyResetMode(config.EffectiveResetMode);

        foreach (var template in ChecklistTemplates.All)
        {
            Templates.Add(new SetupTemplateViewModel(template, SelectTemplate));
        }

        LoadExistingChecklists();

        // Nothing usable on disk means the new-checklist half of that step is the only way
        // forward, so give it a sensible name and a starting point up front.
        if (Existing.All(c => !c.IsReadable))
        {
            SelectTemplate(Templates[0]);
            NewChecklistName = "Preflight";
        }

        StartAtLogon = AppPlatform.SupportsStartAtLogon && LogonTask.IsRegistered();
    }

    public string Hostname { get; }

    public ObservableCollection<SetupChecklistViewModel> Existing { get; } = new();

    public ObservableCollection<SetupTemplateViewModel> Templates { get; } = new();

    public string StepTitle => StepTitles[Step];

    public string StepNumberLabel => $"STEP {Step + 1} OF {StepTitles.Length}";

    public bool IsWelcome => Step == 0;

    public bool IsStation => Step == 1;

    public bool IsServices => Step == 2;

    public bool IsChecklists => Step == 3;

    public bool IsTouches => Step == 4;

    public bool IsDone => Step == LastStep;

    public bool IsLastStep => Step == LastStep;

    public bool CanGoBack => Step > 0;

    public string NextLabel => Step switch
    {
        0 => "Let's go",
        4 => "Finish setup",
        _ => "Next",
    };

    public bool HasFinishError => !string.IsNullOrEmpty(FinishError);

    public bool SupportsStartAtLogon => AppPlatform.SupportsStartAtLogon;

    /// <summary>Where the files this walkthrough writes will land — worth showing, not hiding.</summary>
    public string ChecklistsFolder => _checklists.Directory;

    public string ConfigPath => _stationLoader.FilePath;

    /// <summary>The file a new checklist would be written to, shown live as they type the name.</summary>
    public string NewChecklistFileName => string.IsNullOrWhiteSpace(NewChecklistName)
        ? string.Empty
        : ChecklistWriter.FileNameFor(NewChecklistName);

    public SetupTemplateViewModel? ChosenTemplate => Templates.FirstOrDefault(t => t.IsSelected);

    /// <summary>
    /// How many tabs this station will have when the walkthrough finishes. Drives the "you have
    /// nothing selected" nudge, which is a warning rather than a block — an operator is allowed to
    /// set the station up now and build its checklists later.
    /// </summary>
    public int SelectedCount => Existing.Count(c => c.IsSelected)
        + (ChosenTemplate is not null && !string.IsNullOrWhiteSpace(NewChecklistName) ? 1 : 0);

    public bool HasNothingSelected => SelectedCount == 0;

    /// <summary>Raised when the walkthrough is over, so the window can close and the app reload.</summary>
    public event EventHandler? Finished;

    /// <summary>
    /// Raised when the person wants the guided tour after setting up. Separate from
    /// <see cref="Finished"/> because the window has to close either way, and only the view knows
    /// how to start a tour over the real windows.
    /// </summary>
    public event EventHandler? TourRequested;

    /// <summary>
    /// Finishes setup and then walks them round the real interface. The two are complementary:
    /// this screen got the station configured without ever showing them the app.
    /// </summary>
    [RelayCommand]
    private void StartTour()
    {
        SetupState.MarkDone(skipped: false);
        TourRequested?.Invoke(this, EventArgs.Empty);
        Finished?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Next()
    {
        if (Step >= LastStep)
        {
            Close();
            return;
        }

        if (Step == 4)
        {
            if (!Apply())
            {
                return;
            }
        }

        Step++;
    }

    [RelayCommand]
    private void Back()
    {
        if (Step > 0)
        {
            Step--;
        }
    }

    /// <summary>
    /// Leaves without writing anything, and records that so it does not reappear every launch.
    /// The station keeps whatever it already had.
    /// </summary>
    [RelayCommand]
    private void Skip()
    {
        SetupState.MarkDone(skipped: true);
        Finished?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Close()
    {
        SetupState.MarkDone(skipped: false);
        Finished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds one blank checklist row's worth of intent: pick a template, name it.</summary>
    private void SelectTemplate(SetupTemplateViewModel choice)
    {
        foreach (var template in Templates)
        {
            template.IsSelected = ReferenceEquals(template, choice);
        }

        if (string.IsNullOrWhiteSpace(NewChecklistName))
        {
            NewChecklistName = choice.Template.Key == "shutdown" ? "After service" : "Preflight";
        }

        OnPropertyChanged(nameof(ChosenTemplate));
        RaiseSelectionChanged();
    }

    partial void OnNewChecklistNameChanged(string value) => RaiseSelectionChanged();

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasNothingSelected));
    }

    private void LoadExistingChecklists()
    {
        Existing.Clear();

        foreach (var file in _checklists.ListFiles())
        {
            ChecklistDefinition? definition = null;
            try
            {
                definition = _checklists.Load(file);
            }
            catch (Exception)
            {
                // Shown as unusable rather than hidden: a file that will not parse is something
                // the person setting this up needs to know about.
            }

            var row = new SetupChecklistViewModel(file, definition, RaiseSelectionChanged);

            // Pre-tick what this station already loads, and on a fresh install the samples that
            // were written for this hostname — which is the zero-config path working as intended.
            row.IsSelected = definition is not null
                && (_config.Checklists.Contains(file, StringComparer.OrdinalIgnoreCase)
                    || (_config.Checklists.Count == 0
                        && string.Equals(definition.Station, StationName, StringComparison.OrdinalIgnoreCase)));

            Existing.Add(row);
        }
    }

    private void ApplyResetMode(string mode)
    {
        ResetEveryLaunch = mode == ResetModes.EveryLaunch;
        ResetPowerCycle = mode == ResetModes.PowerCycle;
        ResetDaily = mode == ResetModes.Daily;
    }

    /// <summary>
    /// Writes everything: the new checklist file if one was chosen, then <c>station.json</c>. Any
    /// failure is reported on the step rather than thrown, because a station.json that cannot be
    /// written is usually a folder permission problem and the person can still fix it.
    /// </summary>
    private bool Apply()
    {
        FinishError = null;

        var station = string.IsNullOrWhiteSpace(StationName) ? Hostname : StationName.Trim();
        var tabs = new List<string>();
        var created = new List<string>();

        if (ChosenTemplate is { } chosen && !string.IsNullOrWhiteSpace(NewChecklistName))
        {
            var fileName = ChecklistWriter.FileNameFor(NewChecklistName);
            var definition = chosen.Template.Build(station, NewChecklistName.Trim());

            if (ChecklistTemplates.IsAfterService(chosen.Template))
            {
                definition.CountsTowardReady = false;
                definition.OpenAfterService = true;
            }

            try
            {
                // Never clobber: a name that collides with a file already there gets a suffix,
                // because losing somebody's existing checklist to a wizard would be unforgivable.
                fileName = UniqueFileName(fileName);
                _writer.Save(definition, fileName);
                created.Add(fileName);
                tabs.Add(fileName);
            }
            catch (Exception ex)
            {
                FinishError = $"Could not write {fileName}: {ex.Message}";
                return false;
            }
        }

        tabs.AddRange(Existing.Where(c => c.IsSelected).Select(c => c.FileName));

        _config.Station = station;
        _config.Operator = string.IsNullOrWhiteSpace(OperatorName) ? null : OperatorName.Trim();
        _config.Checklists = tabs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _config.ResetMode = ResetPowerCycle ? ResetModes.PowerCycle
            : ResetDaily ? ResetModes.Daily
            : ResetModes.EveryLaunch;
        _config.Service = new ServiceTimes
        {
            Starts = ServiceTimes
                .Split('\n')
                .Select(l => l.Trim().TrimEnd('\r'))
                .Where(l => l.Length > 0)
                .ToList(),
            ResetLeadMinutes = ResetLeadMinutes,
            StartsAt = null,
            DoorsAt = _config.Service?.DoorsAt,
            StreamAt = _config.Service?.StreamAt,
            Venue = _config.Service?.Venue,
        };

        try
        {
            _stationLoader.Save(_config);
        }
        catch (Exception ex)
        {
            FinishError = $"Could not save {_stationLoader.FilePath}: {ex.Message}";
            return false;
        }

        ApplyLogonPreference();

        // Read back rather than trusting what was just written: the loader is the thing that
        // decides how many tabs this station really ends up with, and telling someone "it will
        // open empty" when it will not is exactly the kind of small lie that erodes trust in
        // the whole screen.
        var effective = _stationLoader.Load();
        Summary = BuildSummary(station, created, effective.Checklists.Count);
        return true;
    }

    private void ApplyLogonPreference()
    {
        if (!AppPlatform.SupportsStartAtLogon)
        {
            return;
        }

        try
        {
            var registered = LogonTask.IsRegistered();
            if (StartAtLogon && !registered)
            {
                var result = LogonTask.Register();
                LogonStatus = result.Message;
                StartAtLogon = LogonTask.IsRegistered();
            }
            else if (!StartAtLogon && registered)
            {
                LogonStatus = LogonTask.Unregister().Message;
                StartAtLogon = LogonTask.IsRegistered();
            }
        }
        catch (Exception ex)
        {
            // Not fatal to setup — the station is configured either way, and this is one toggle
            // in Settings away from being fixed.
            LogonStatus = ex.Message;
        }
    }

    private string UniqueFileName(string fileName)
    {
        if (!_writer.Exists(fileName))
        {
            return fileName;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        for (var n = 2; n < 100; n++)
        {
            var candidate = $"{stem}-{n}.json";
            if (!_writer.Exists(candidate))
            {
                return candidate;
            }
        }

        return $"{stem}-{Guid.NewGuid():N}.json";
    }

    private string BuildSummary(string station, IReadOnlyList<string> created, int tabCount)
    {
        var lines = new List<string> { $"This PC is now “{station}”." };

        lines.Add(tabCount switch
        {
            0 => "No checklists are selected yet, so the app will open with an empty list. "
                + "Press EDIT when you are ready to build one.",
            1 => "It shows one checklist as a tab.",
            _ => $"It shows {tabCount} checklists as tabs.",
        });

        if (created.Count > 0)
        {
            lines.Add($"Created {string.Join(", ", created)} — edit it any time with EDIT in the top bar.");
        }

        var starts = _config.Service?.Starts ?? new List<string>();
        if (starts.Count > 0)
        {
            lines.Add($"Service times: {string.Join(", ", starts)}. The checklist starts again "
                + $"{ResetLeadMinutes} minutes before each one.");
        }

        if (!string.IsNullOrEmpty(LogonStatus))
        {
            lines.Add(LogonStatus);
        }

        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }
}
