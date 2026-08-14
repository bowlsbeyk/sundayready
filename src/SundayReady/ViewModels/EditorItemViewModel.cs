using CommunityToolkit.Mvvm.ComponentModel;
using SundayReady.Models;

namespace SundayReady.ViewModels;

/// <summary>
/// One checklist item, open for editing. Mirrors <see cref="ChecklistItem"/> as flat text
/// fields so the form can bind directly, and folds back into a model on save.
/// </summary>
public sealed partial class EditorItemViewModel : ObservableObject
{
    public const string NoVerifier = "(none)";

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAction), nameof(TypeSummary))]
    private string _type = ChecklistItemTypes.Manual;

    [ObservableProperty]
    private string _section = string.Empty;

    [ObservableProperty]
    private string _actionRun = string.Empty;

    [ObservableProperty]
    private string _actionArgs = string.Empty;

    [ObservableProperty]
    private string _actionLabel = string.Empty;

    /// <summary>Extra launch targets, one per line. This is how one button opens two dashboards.</summary>
    [ObservableProperty]
    private string _actionAlso = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HasVerifier), nameof(ShowProcessName), nameof(ShowUrl), nameof(ShowContains),
        nameof(ShowHost), nameof(ShowPort), nameof(ShowNameContains), nameof(ShowPath), nameof(TypeSummary))]
    private string _verifyKind = NoVerifier;

    [ObservableProperty]
    private string _processName = string.Empty;

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private string _contains = string.Empty;

    [ObservableProperty]
    private string _host = string.Empty;

    [ObservableProperty]
    private string _port = string.Empty;

    [ObservableProperty]
    private string _nameContains = string.Empty;

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private int _maxAttempts = VerifySpec.DefaultMaxAttempts;

    /// <summary>Troubleshooting steps, one per line, shown on the failed-verify screen.</summary>
    [ObservableProperty]
    private string _checkSteps = string.Empty;

    [ObservableProperty]
    private string _remediationLabel = string.Empty;

    [ObservableProperty]
    private string _remediationRun = string.Empty;

    public EditorItemViewModel()
    {
    }

    public EditorItemViewModel(ChecklistItem item)
    {
        _label = item.Label;
        _type = item.Type;
        _section = item.Section ?? string.Empty;

        if (item.Action is { } action)
        {
            _actionRun = action.Run;
            _actionArgs = action.Args ?? string.Empty;
            _actionLabel = action.Label ?? string.Empty;
            _actionAlso = string.Join(Environment.NewLine, action.Also.Select(a => a.Run));
        }

        if (item.Verify is { } verify)
        {
            _verifyKind = string.IsNullOrWhiteSpace(verify.Kind) ? NoVerifier : verify.Kind;
            _processName = verify.ProcessName ?? string.Empty;
            _url = verify.Url ?? string.Empty;
            _contains = verify.Contains ?? string.Empty;
            _host = verify.Host ?? string.Empty;
            _port = verify.Port?.ToString() ?? string.Empty;
            _nameContains = verify.NameContains ?? string.Empty;
            _path = verify.Path ?? string.Empty;
            _maxAttempts = verify.MaxAttempts;
        }

        _checkSteps = string.Join(Environment.NewLine, item.CheckSteps);
        _remediationLabel = item.RemediationLabel ?? string.Empty;
        _remediationRun = item.Remediation?.Run ?? string.Empty;
    }

    public static IReadOnlyList<string> ItemTypes { get; } = new[]
    {
        ChecklistItemTypes.Manual,
        ChecklistItemTypes.Action,
        ChecklistItemTypes.Verified,
    };

    public bool ShowAction => string.Equals(Type, ChecklistItemTypes.Action, StringComparison.OrdinalIgnoreCase);

    public bool HasVerifier => !string.Equals(VerifyKind, NoVerifier, StringComparison.OrdinalIgnoreCase)
                               && !string.IsNullOrWhiteSpace(VerifyKind);

    public bool ShowProcessName => Is("processRunning");

    public bool ShowUrl => Is("httpContains");

    public bool ShowContains => Is("httpContains");

    public bool ShowHost => Is("internetReachable") || Is("hostReachable");

    public bool ShowPort => Is("hostReachable");

    public bool ShowNameContains => Is("audioDevicePresent");

    public bool ShowPath => Is("fileExists");

    /// <summary>The one-line description shown next to the item in the list.</summary>
    public string TypeSummary => HasVerifier
        ? $"{Type} · {VerifyKind}"
        : Type;

    private bool Is(string kind) => string.Equals(VerifyKind, kind, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What is wrong with this item, or null. Checked before a file is written so a station
    /// never loads a checklist that the editor itself knew was broken.
    /// </summary>
    public string? Problem
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Label))
            {
                return "This item needs a label.";
            }

            if (ShowAction && string.IsNullOrWhiteSpace(ActionRun))
            {
                return "An action item needs something to run.";
            }

            if (!HasVerifier)
            {
                return string.Equals(Type, ChecklistItemTypes.Verified, StringComparison.OrdinalIgnoreCase)
                    ? "A verified item needs a verifier — otherwise nothing can ever check it."
                    : null;
            }

            if (ShowProcessName && string.IsNullOrWhiteSpace(ProcessName)) return "processRunning needs a process name.";
            if (ShowUrl && string.IsNullOrWhiteSpace(Url)) return "httpContains needs a URL.";
            if (ShowContains && string.IsNullOrEmpty(Contains)) return "httpContains needs the text to look for.";
            if (ShowHost && Is("hostReachable") && string.IsNullOrWhiteSpace(Host)) return "hostReachable needs an address to reach.";
            if (ShowPort && !string.IsNullOrWhiteSpace(Port) && !int.TryParse(Port, out var parsed)) return "Port must be a number, or empty to just ping.";
            if (ShowPort && int.TryParse(Port, out var range) && (range < 1 || range > 65535)) return "Port must be between 1 and 65535.";
            if (ShowNameContains && string.IsNullOrWhiteSpace(NameContains)) return "audioDevicePresent needs a device name to match.";
            if (ShowPath && string.IsNullOrWhiteSpace(Path)) return "fileExists needs a path.";

            return null;
        }
    }

    public bool HasProblem => Problem is not null;

    public ChecklistItem ToModel()
    {
        var item = new ChecklistItem
        {
            Label = Label.Trim(),
            Type = Type,
            Section = Blank(Section),
            CheckSteps = SplitLines(CheckSteps),
        };

        if (ShowAction && !string.IsNullOrWhiteSpace(ActionRun))
        {
            item.Action = new ActionSpec
            {
                Run = ActionRun.Trim(),
                Args = Blank(ActionArgs),
                Label = Blank(ActionLabel),
                Also = SplitLines(ActionAlso).Select(run => new ActionSpec { Run = run }).ToList(),
            };
        }

        if (HasVerifier)
        {
            item.Verify = new VerifySpec
            {
                Kind = VerifyKind,
                MaxAttempts = MaxAttempts < 1 ? VerifySpec.DefaultMaxAttempts : MaxAttempts,
                ProcessName = ShowProcessName ? Blank(ProcessName) : null,
                Url = ShowUrl ? Blank(Url) : null,
                Contains = ShowContains ? Blank(Contains) : null,
                Host = ShowHost ? Blank(Host) : null,
                Port = ShowPort && int.TryParse(Port, out var port) ? port : null,
                NameContains = ShowNameContains ? Blank(NameContains) : null,
                Path = ShowPath ? Blank(Path) : null,
            };
        }

        if (!string.IsNullOrWhiteSpace(RemediationRun))
        {
            item.Remediation = new ActionSpec { Run = RemediationRun.Trim() };
            item.RemediationLabel = Blank(RemediationLabel) ?? "Run fix";
        }

        return item;
    }

    /// <summary>Re-evaluates the computed validation and summary after any field edit.</summary>
    public void NotifyDerived()
    {
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(HasProblem));
        OnPropertyChanged(nameof(TypeSummary));
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> SplitLines(string value) => value
        .Split('\n')
        .Select(line => line.Trim().TrimEnd('\r'))
        .Where(line => line.Length > 0)
        .ToList();
}
