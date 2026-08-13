using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SundayReady.Services;

namespace SundayReady.ViewModels;

/// <summary>One row of the log table.</summary>
public sealed class LogRowViewModel
{
    public LogRowViewModel(LogRecord record)
    {
        Record = record;
        Time = record.Timestamp.ToString("HH:mm:ss");
        By = string.IsNullOrWhiteSpace(record.Initials) || record.Initials == "—" ? "—" : record.Initials;

        // An override shows the operator's own words; a failure shows the verifier's.
        Secondary = record.IsOverride && record.Detail is not null
            ? $"“{record.Detail}”"
            : record.IsFailure ? record.Detail : null;
    }

    public LogRecord Record { get; }

    public string Time { get; }

    public string By { get; }

    public string Item => Record.Item;

    public string How => Record.HowDisplay;

    public string? Secondary { get; }

    public bool HasSecondary => !string.IsNullOrEmpty(Secondary);

    public bool IsFailure => Record.IsFailure;

    public bool IsOverride => Record.IsOverride;

    public bool IsAuto => Record.How == LogHow.Auto;
}

/// <summary>
/// The accountability trail, and the moment the operator declares themselves ready.
/// Rows are read back off the log file rather than kept in memory, so a restart mid-morning
/// does not lose the record of what already happened.
/// </summary>
public sealed partial class CompletionLogViewModel : ObservableObject
{
    private readonly StationViewModel _station;
    private readonly CompletionLogger _logger;

    [ObservableProperty]
    private string _initials = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string _signOffStatus = string.Empty;

    public CompletionLogViewModel(StationViewModel station)
    {
        _station = station;
        _logger = station.Logger;
        _initials = station.OperatorInitials ?? string.Empty;

        Refresh();
    }

    public ObservableCollection<LogRowViewModel> Rows { get; } = new();

    public string Station => _station.StationName;

    public string Header => $"{DateTime.Now:dddd d MMM yyyy} · {_station.StationName}".ToUpperInvariant();

    public string FilePathLine => $"WRITING TO {_logger.FilePathFor(_station.StationName, DateTime.Now).ToUpperInvariant()}";

    public bool IsEmpty => Rows.Count == 0;

    public int OpenItems => _station.ItemsLeft;

    public int Overridden => _station.OverriddenCount;

    public bool WouldBePartial => OpenItems > 0 || Overridden > 0;

    public string Advisory => WouldBePartial
        ? $"{OpenItems} item{(OpenItems == 1 ? "" : "s")} still open, {Overridden} overridden. Signing off like this marks the service partial."
        : "Everything on every tab is checked and verified. Signing off records a clean service.";

    public string SignOffExplanation =>
        "Your initials go on this service. They are written to the log alongside everything the app did and everything you decided.";

    private bool CanSignOff => !string.IsNullOrWhiteSpace(Initials);

    [RelayCommand(CanExecute = nameof(CanSignOff))]
    private void SignOff()
    {
        var initials = Initials.Trim().ToUpperInvariant();
        _station.SignOff(initials, Notes);

        SignOffStatus = WouldBePartial
            ? $"Signed off by {initials} — recorded as partial."
            : $"Signed off by {initials}.";

        Refresh();
    }

    [RelayCommand]
    private void Export() => OpenPath(AppPaths.LogsDirectory);

    [RelayCommand]
    private void Refresh()
    {
        Rows.Clear();
        foreach (var record in _logger.Read(_station.StationName, DateTime.Now))
        {
            Rows.Add(new LogRowViewModel(record));
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(OpenItems));
        OnPropertyChanged(nameof(Overridden));
        OnPropertyChanged(nameof(WouldBePartial));
        OnPropertyChanged(nameof(Advisory));
    }

    partial void OnInitialsChanged(string value) => SignOffCommand.NotifyCanExecuteChanged();

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
}
