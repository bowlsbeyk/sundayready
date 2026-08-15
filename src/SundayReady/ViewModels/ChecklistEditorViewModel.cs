using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SundayReady.Models;
using SundayReady.Services;

namespace SundayReady.ViewModels;

/// <summary>One checklist file, open for editing. Rendered as one tab on the station screen.</summary>
public sealed partial class EditorFileViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string _tabName = string.Empty;

    [ObservableProperty]
    private string _station = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private bool _isDirty;

    /// <summary>Whether station.json lists this file, i.e. whether this PC shows it as a tab.</summary>
    [ObservableProperty]
    private bool _isLoadedHere;

    /// <summary>Untick for a shutdown list so it does not hold the Ready to go gate shut.</summary>
    [ObservableProperty]
    private bool _countsTowardReady = true;

    /// <summary>The list "Service finished" opens.</summary>
    [ObservableProperty]
    private bool _openAfterService;

    [ObservableProperty]
    private EditorItemViewModel? _selectedItem;

    public EditorFileViewModel(string fileName, ChecklistDefinition? definition, string? loadError = null)
    {
        FileName = fileName;
        LoadError = loadError;

        _tabName = definition?.TabLabel ?? System.IO.Path.GetFileNameWithoutExtension(fileName);
        _station = definition?.Station ?? string.Empty;
        _countsTowardReady = definition?.CountsTowardReady ?? true;
        _openAfterService = definition?.OpenAfterService ?? false;

        foreach (var item in definition?.Items ?? new List<ChecklistItem>())
        {
            Add(new EditorItemViewModel(item));
        }

        _selectedItem = Items.FirstOrDefault();
    }

    public string FileName { get; }

    public string? LoadError { get; }

    public bool IsBroken => LoadError is not null;

    public ObservableCollection<EditorItemViewModel> Items { get; } = new();

    public string Title => IsDirty ? $"{TabName} •" : TabName;

    public string ItemCountText => $"{Items.Count} item{(Items.Count == 1 ? "" : "s")}";

    public string? FirstProblem => Items.Select(i => i.Problem).FirstOrDefault(p => p is not null);

    public Action? Changed { get; set; }

    partial void OnIsLoadedHereChanged(bool value) => Changed?.Invoke();

    partial void OnTabNameChanged(string value) => MarkDirty();

    partial void OnStationChanged(string value) => MarkDirty();

    partial void OnCountsTowardReadyChanged(bool value) => MarkDirty();

    partial void OnOpenAfterServiceChanged(bool value) => MarkDirty();

    private void Add(EditorItemViewModel item)
    {
        item.PropertyChanged += OnItemChanged;
        Items.Add(item);
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorItemViewModel.Problem) or nameof(EditorItemViewModel.HasProblem)
            or nameof(EditorItemViewModel.TypeSummary))
        {
            return;
        }

        (sender as EditorItemViewModel)?.NotifyDerived();
        IsDirty = true;
        Changed?.Invoke();
    }

    [RelayCommand]
    private void AddItem()
    {
        var item = new EditorItemViewModel
        {
            Label = "New item",
            // Inherit the section of whatever is selected: items are usually added in runs.
            Section = SelectedItem?.Section ?? string.Empty,
        };

        item.PropertyChanged += OnItemChanged;

        var at = SelectedItem is null ? Items.Count : Items.IndexOf(SelectedItem) + 1;
        Items.Insert(at, item);

        SelectedItem = item;
        MarkDirty();
    }

    [RelayCommand]
    private void DeleteItem()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        var at = Items.IndexOf(item);
        item.PropertyChanged -= OnItemChanged;
        Items.Remove(item);

        SelectedItem = Items.ElementAtOrDefault(Math.Min(at, Items.Count - 1));
        MarkDirty();
    }

    [RelayCommand]
    private void MoveUp() => Move(-1);

    [RelayCommand]
    private void MoveDown() => Move(1);

    private void Move(int delta)
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        var from = Items.IndexOf(item);
        var to = from + delta;

        if (to < 0 || to >= Items.Count)
        {
            return;
        }

        Items.Move(from, to);
        SelectedItem = item;
        MarkDirty();
    }

    public void MarkDirty()
    {
        IsDirty = true;
        OnPropertyChanged(nameof(ItemCountText));
        Changed?.Invoke();
    }

    public ChecklistDefinition ToModel() => new()
    {
        Station = Station.Trim(),
        Name = string.IsNullOrWhiteSpace(TabName) ? null : TabName.Trim(),
        CountsTowardReady = CountsTowardReady,
        OpenAfterService = OpenAfterService,
        Items = Items.Select(i => i.ToModel()).ToList(),
    };
}

/// <summary>
/// Builds and edits the checklist files on this PC, so a station can be set up without ever
/// opening a JSON file. Saving writes the file; the station is watching the folder, so the
/// running checklist updates as soon as it lands.
/// </summary>
public sealed partial class ChecklistEditorViewModel : ObservableObject
{
    private readonly ChecklistLoader _loader;
    private readonly ChecklistWriter _writer;
    private readonly StationConfigLoader _stationLoader;
    private readonly StationConfig _config;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private EditorFileViewModel? _selectedFile;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private string _newFileName = string.Empty;

    public ChecklistEditorViewModel(
        StationConfig config,
        ChecklistLoader loader,
        ChecklistWriter writer,
        StationConfigLoader stationLoader,
        VerifierRegistry registry)
    {
        _config = config;
        _loader = loader;
        _writer = writer;
        _stationLoader = stationLoader;

        VerifierKinds = new[] { EditorItemViewModel.NoVerifier }
            .Concat(registry.Kinds.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            .ToList();

        LoadFiles();
    }

    public IReadOnlyList<string> VerifierKinds { get; }

    public IReadOnlyList<string> ItemTypes => EditorItemViewModel.ItemTypes;

    public ObservableCollection<EditorFileViewModel> Files { get; } = new();

    public bool HasSelection => SelectedFile is not null;

    public string Directory => _writer.Directory;

    public bool AnyDirty => Files.Any(f => f.IsDirty);

    private void LoadFiles()
    {
        Files.Clear();

        foreach (var fileName in _loader.ListFiles())
        {
            EditorFileViewModel file;
            try
            {
                file = new EditorFileViewModel(fileName, _loader.Load(fileName));
            }
            catch (ChecklistLoadException ex)
            {
                // Surface it rather than hide it, but do not let the editor overwrite a file
                // it could not read — that would turn a typo into lost work.
                file = new EditorFileViewModel(fileName, null, ex.Message);
            }
            catch (Exception ex)
            {
                file = new EditorFileViewModel(fileName, null, ex.Message);
            }

            file.IsLoadedHere = _config.Checklists.Contains(fileName, StringComparer.OrdinalIgnoreCase);
            file.Changed = () => OnPropertyChanged(nameof(AnyDirty));
            Files.Add(file);
        }

        SelectFile(Files.FirstOrDefault());
    }

    private void SelectFile(EditorFileViewModel? file) => SelectedFile = file;

    [RelayCommand]
    private void NewFile()
    {
        var name = string.IsNullOrWhiteSpace(NewFileName) ? "New checklist" : NewFileName.Trim();
        var fileName = ChecklistWriter.FileNameFor(name);

        if (_writer.Exists(fileName))
        {
            Status = $"{fileName} already exists.";
            return;
        }

        var file = new EditorFileViewModel(fileName, new ChecklistDefinition
        {
            Station = string.IsNullOrWhiteSpace(_config.Station) ? Environment.MachineName : _config.Station,
            Name = name,
        })
        {
            IsLoadedHere = true,
        };

        file.Changed = () => OnPropertyChanged(nameof(AnyDirty));
        file.MarkDirty();

        Files.Add(file);
        SelectFile(file);

        NewFileName = string.Empty;
        Status = $"Created {fileName}. It is not on disk until you save.";
    }

    [RelayCommand]
    private void DeleteFile()
    {
        if (SelectedFile is not { } file)
        {
            return;
        }

        try
        {
            _writer.Delete(file.FileName);
            _config.Checklists.RemoveAll(c => string.Equals(c, file.FileName, StringComparison.OrdinalIgnoreCase));
            _stationLoader.Save(_config);

            Files.Remove(file);
            SelectFile(Files.FirstOrDefault());
            Status = $"Deleted {file.FileName}.";
        }
        catch (Exception ex)
        {
            Status = $"Could not delete {file.FileName}: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Save()
    {
        var saved = 0;
        var problems = new List<string>();

        foreach (var file in Files.Where(f => f.IsDirty))
        {
            if (file.IsBroken)
            {
                problems.Add($"{file.FileName} did not load, so it was not overwritten.");
                continue;
            }

            if (file.FirstProblem is { } problem)
            {
                problems.Add($"{file.FileName}: {problem}");
                continue;
            }

            try
            {
                _writer.Save(file.ToModel(), file.FileName);
                file.IsDirty = false;
                saved++;
            }
            catch (Exception ex)
            {
                problems.Add($"{file.FileName}: {ex.Message}");
            }
        }

        SaveStationAssignment();
        OnPropertyChanged(nameof(AnyDirty));

        Status = problems.Count > 0
            ? string.Join("  ", problems)
            : saved == 0 ? "Nothing to save." : $"Saved {saved} file{(saved == 1 ? "" : "s")}. The station picked it up.";
    }

    /// <summary>
    /// Writes the tick-boxes back to station.json. Order follows the file list, so the tabs
    /// on the station screen appear in the order shown here.
    /// </summary>
    private void SaveStationAssignment()
    {
        var wanted = Files.Where(f => f.IsLoadedHere).Select(f => f.FileName).ToList();

        if (wanted.SequenceEqual(_config.Checklists, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _config.Checklists = wanted;

        try
        {
            _stationLoader.Save(_config);
        }
        catch (Exception ex)
        {
            Status = $"Checklists saved, but station.json could not be updated: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Revert()
    {
        LoadFiles();
        OnPropertyChanged(nameof(AnyDirty));
        Status = "Reloaded from disk. Unsaved changes were discarded.";
    }
}
