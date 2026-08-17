using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SundayReady.Models;
using SundayReady.Services;

namespace SundayReady.ViewModels;

/// <summary>Where a verifier is in its life cycle.</summary>
public enum VerifyStatus
{
    /// <summary>No verifier, or an action item whose command has not been launched yet.</summary>
    Unknown,

    /// <summary>Polling and not passing yet. Still inside its retry budget.</summary>
    Polling,

    Passed,

    Failed,

    /// <summary>The JSON asked for a <c>kind</c> no verifier claims — almost always a typo.</summary>
    Unsupported,
}

/// <summary>How the row draws. Maps 1:1 onto the five treatments in the handoff.</summary>
public enum ItemRowState
{
    Done,
    Open,
    Launchable,
    Polling,
    Failed,
}

/// <summary>One tickable step inside an item.</summary>
public sealed partial class SubStepViewModel : ObservableObject
{
    private readonly Action<SubStepViewModel, bool> _changed;

    [ObservableProperty]
    private bool _isDone;

    public SubStepViewModel(string label, bool done, Action<SubStepViewModel, bool> changed)
    {
        Label = label;
        _isDone = done;
        _changed = changed;
    }

    public string Label { get; }

    [RelayCommand]
    private void Toggle() => IsDone = !IsDone;

    partial void OnIsDoneChanged(bool value) => _changed(this, value);
}

/// <summary>What the station needs to know when an item moves.</summary>
public interface IChecklistHost
{
    void ItemChanged(ChecklistItemViewModel item, string how, string? detail, TimeSpan? duration);

    /// <summary>Opens the instructions or sub-steps for an item.</summary>
    void OpenItemDetail(ChecklistItemViewModel item);

    bool IsSubStepDone(string key);

    void SetSubStepDone(string key, string itemLabel, string subStep, bool done);

    void OpenFailedDetail(ChecklistItemViewModel item);

    void OpenOverride(ChecklistItemViewModel item);
}

public sealed partial class ChecklistItemViewModel : ObservableObject
{
    private readonly ProcessLauncher _launcher;
    private readonly IChecklistHost _host;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowState), nameof(SubLine), nameof(TypeTag), nameof(ShowLaunchButton),
        nameof(ShowRelaunchChip), nameof(ShowTypeTag), nameof(ShowFailureButtons), nameof(ShowSubLine),
        nameof(IsRowDone), nameof(IsRowFailed), nameof(IsRowPolling), nameof(IsRowLaunchable),
        nameof(IsRowEmpty), nameof(IsFailing))]
    private bool _isChecked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowState), nameof(SubLine), nameof(TypeTag), nameof(ShowLaunchButton),
        nameof(ShowRelaunchChip), nameof(ShowTypeTag), nameof(ShowFailureButtons), nameof(ShowSubLine),
        nameof(IsRowDone), nameof(IsRowFailed), nameof(IsRowPolling), nameof(IsRowLaunchable),
        nameof(IsRowEmpty), nameof(IsFailing))]
    private VerifyStatus _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubLine))]
    private int _attempts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubLine))]
    private string? _launchError;

    /// <summary>True once the operator has pressed Launch. Until then a failing verify just means "not started".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowState), nameof(SubLine))]
    private bool _hasLaunched;

    public ChecklistItemViewModel(
        ChecklistItem item,
        ChecklistDefinition source,
        ProcessLauncher launcher,
        IChecklistHost host,
        IVerifier? verifier,
        ItemState? restored)
    {
        Item = item;
        Source = source;
        Verifier = verifier;
        _launcher = launcher;
        _host = host;

        if (item.Verify is null)
        {
            _status = VerifyStatus.Unknown;
        }
        else if (verifier is null)
        {
            _status = VerifyStatus.Unsupported;
        }
        else
        {
            // Pure verified items poll from startup. Action items also poll — that is how an
            // already-running vMix ticks itself — but stay Unknown until launched, so an
            // unlaunched item reads as "press this", not as a failure.
            _status = IsActionType ? VerifyStatus.Unknown : VerifyStatus.Polling;
        }

        foreach (var label in item.SubSteps)
        {
            var key = SubStepKey(label);
            SubSteps.Add(new SubStepViewModel(label, host.IsSubStepDone(key), OnSubStepChanged));
        }

        if (restored is not null)
        {
            // Straight to backing fields: restoring today's saved state is not a completion
            // event and must not re-log it.
            _isChecked = restored.Checked;
            CheckedBy = restored.CheckedBy;
            CheckedAt = restored.CheckedAt;
            CompletionSource = restored.Source;
            OverrideNote = restored.OverrideNote;

            if (restored.Checked && restored.Source == CompletionSources.Override)
            {
                _status = VerifyStatus.Unknown;
            }
        }
    }

    partial void OnStatusChanged(VerifyStatus value)
    {
        FailingSince = value == VerifyStatus.Failed ? FailingSince ?? DateTimeOffset.Now : null;
    }

    public ChecklistItem Item { get; }

    public ChecklistDefinition Source { get; }

    public IVerifier? Verifier { get; }

    public string Label => Item.Label;

    public string StateKey => DailyStateStore.KeyFor(Source.SourceFile, Item.Label);

    public string TabLabel => Source.TabLabel;

    public bool IsVerifiedType => string.Equals(Item.Type, ChecklistItemTypes.Verified, StringComparison.OrdinalIgnoreCase);

    public bool IsActionType => string.Equals(Item.Type, ChecklistItemTypes.Action, StringComparison.OrdinalIgnoreCase);

    public bool HasAction => Item.Action is not null;

    public bool HasVerify => Item.Verify is not null;

    /// <summary>A verified item is the app's to decide; the operator cannot tick it by hand.</summary>
    public bool CanToggle => !IsVerifiedType;

    public string? CheckedBy { get; private set; }

    public DateTimeOffset? CheckedAt { get; private set; }

    public string CompletionSource { get; private set; } = CompletionSources.Manual;

    public string? OverrideNote { get; private set; }

    public bool IsOverridden => IsChecked && CompletionSource == CompletionSources.Override;

    public TimeSpan LastDuration { get; private set; }

    public string? LastResult { get; private set; }

    public DateTimeOffset? LastPassAt { get; private set; }

    /// <summary>
    /// When this item first went red and stayed red. The techdesk headlines how long a thing
    /// has been broken, and "failing for 26 minutes" is a different conversation from
    /// "failing since the last poll".
    /// </summary>
    public DateTimeOffset? FailingSince { get; private set; }

    public int MaxAttempts => Item.Verify?.MaxAttempts ?? VerifySpec.DefaultMaxAttempts;

    public string LaunchLabel => Item.Action?.Label ?? "Launch";

    public bool IsFailing => Status == VerifyStatus.Failed && !IsChecked;

    public ItemRowState RowState
    {
        get
        {
            if (IsChecked) return ItemRowState.Done;
            if (Status == VerifyStatus.Failed) return ItemRowState.Failed;
            if (Status == VerifyStatus.Polling) return ItemRowState.Polling;
            if (HasAction) return ItemRowState.Launchable;
            return ItemRowState.Open;
        }
    }

    // Style-class bindings. XAML selects on booleans, not on enum equality.
    public bool IsRowDone => RowState == ItemRowState.Done;

    public bool IsRowFailed => RowState == ItemRowState.Failed;

    public bool IsRowPolling => RowState == ItemRowState.Polling;

    public bool IsRowLaunchable => RowState == ItemRowState.Launchable;

    /// <summary>Open and launchable rows both draw the same empty outlined slot.</summary>
    public bool IsRowEmpty => RowState is ItemRowState.Open or ItemRowState.Launchable;

    public bool ShowLaunchButton => HasAction && !IsChecked && RowState != ItemRowState.Failed;

    public bool ShowRelaunchChip => HasAction && IsChecked;

    public bool ShowFailureButtons => RowState == ItemRowState.Failed;

    public bool ShowTypeTag => !ShowLaunchButton && !ShowRelaunchChip;

    public bool ShowSubLine => !string.IsNullOrEmpty(SubLine);

    public ObservableCollection<SubStepViewModel> SubSteps { get; } = new();

    public bool HasInstructions => Item.Instructions.Count > 0;

    public bool HasSubSteps => SubSteps.Count > 0;

    /// <summary>Whether the row offers a way into a detail panel at all.</summary>
    public bool HasDetail => HasInstructions || HasSubSteps;

    public int SubStepsDone => SubSteps.Count(s => s.IsDone);

    /// <summary>"HOW?" for instructions, "2/5" once there are steps to count.</summary>
    public string DetailChip => HasSubSteps ? $"{SubStepsDone}/{SubSteps.Count}" : "HOW?";

    [RelayCommand]
    private void OpenDetail() => _host.OpenItemDetail(this);

    /// <summary>The right-hand mono tag: MANUAL, AUTO, or WAITING while a poll is in flight.</summary>
    public string TypeTag => Status switch
    {
        VerifyStatus.Polling => "WAITING",
        _ when IsVerifiedType || HasVerify => "AUTO",
        _ => "MANUAL",
    };

    /// <summary>
    /// The mechanism made visible — the verifier's own words about what it just did.
    /// </summary>
    public string SubLine
    {
        get
        {
            if (!string.IsNullOrEmpty(LaunchError))
            {
                return $"launch failed · {LaunchError}";
            }

            if (Status == VerifyStatus.Unsupported)
            {
                return $"unknown verifier kind \"{Item.Verify?.Kind}\" — this item stays manual";
            }

            if (IsOverridden)
            {
                return $"overridden by {CheckedBy} · \"{OverrideNote}\"";
            }

            if (IsChecked && Verifier is not null && LastPassAt is not null)
            {
                return $"verified · {Verifier.Describe(Item.Verify!)} · {FormatDuration(LastDuration)}";
            }

            return Status switch
            {
                VerifyStatus.Polling => $"polling {Item.Verify?.Kind} · retry {Attempts} of {MaxAttempts}",
                VerifyStatus.Failed => FailureLine(),
                _ when HasAction => LaunchLine(),
                _ => string.Empty,
            };
        }
    }

    /// <summary>
    /// Sub-second checks are the common case — a ping or a local HTTP call — and "0.0s"
    /// tells the operator nothing. Show milliseconds until a check is actually slow.
    /// </summary>
    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalSeconds < 1 ? $"{duration.TotalMilliseconds:0} ms" : $"{duration.TotalSeconds:0.0}s";

    private string FailureLine()
    {
        var line = $"FAILED · {Verifier?.Describe(Item.Verify!)} — {LastResult}.";

        return LastPassAt is { } passed
            ? $"{line} Last pass {passed:h:mm tt}, {Attempts} attempts since."
            : $"{line} {Attempts} attempts, never passed today.";
    }

    private string LaunchLine()
    {
        var action = Item.Action!;
        var target = action.Also.Count > 0
            ? $"launches {action.Also.Count + 1} targets"
            : $"launches · {action.Run}";

        return HasVerify ? $"{target} · then verifies" : $"{target} · then confirm";
    }

    /// <summary>Manual and action items toggle on a click anywhere in the row.</summary>
    [RelayCommand]
    private void Toggle()
    {
        if (!CanToggle)
        {
            return;
        }

        SetChecked(!IsChecked, CompletionSources.Manual, null, null);
    }

    [RelayCommand]
    private void Launch()
    {
        if (Item.Action is null)
        {
            return;
        }

        var result = _launcher.Launch(Item.Action);
        LaunchError = result.Succeeded ? null : result.Error;

        if (!result.Succeeded)
        {
            return;
        }

        HasLaunched = true;

        if (HasVerify && Status == VerifyStatus.Unknown)
        {
            // Launch starts the retry budget; the item ticks itself when the verifier agrees.
            Status = VerifyStatus.Polling;
            Attempts = 0;
        }
    }

    [RelayCommand]
    private void Remediate()
    {
        if (Item.Remediation is null)
        {
            return;
        }

        var result = _launcher.Launch(Item.Remediation);
        LaunchError = result.Succeeded ? null : result.Error;
    }

    /// <summary>Clears the retry budget so the next poll starts the count again.</summary>
    [RelayCommand]
    private void RetryNow()
    {
        if (!HasVerify)
        {
            return;
        }

        Attempts = 0;
        Status = VerifyStatus.Polling;
    }

    [RelayCommand]
    private void WhatToCheck() => _host.OpenFailedDetail(this);

    [RelayCommand]
    private void Override() => _host.OpenOverride(this);

    public void ApplyOverride(string initials, string note)
    {
        SetChecked(true, CompletionSources.Override, initials, note);
    }

    public string SubStepKey(string subStep) => $"{StateKey} › {subStep}";

    /// <summary>
    /// A sub-step moving persists it, and completing the last one carries the parent with it —
    /// which is the point of grouping them under one row.
    /// </summary>
    private void OnSubStepChanged(SubStepViewModel step, bool done)
    {
        _host.SetSubStepDone(SubStepKey(step.Label), Label, step.Label, done);

        OnPropertyChanged(nameof(SubStepsDone));
        OnPropertyChanged(nameof(DetailChip));

        if (done && SubSteps.All(s => s.IsDone) && !IsChecked)
        {
            SetChecked(true, CompletionSources.Manual, null, null);
        }
    }

    /// <summary>
    /// Identifies this item's verifier configuration. Two items with the same fingerprint are
    /// asking reality the same question, so their poll state is interchangeable.
    /// </summary>
    public string VerifyFingerprint => Item.Verify is null
        ? string.Empty
        : string.Join("|", Item.Verify.DescribeFields().Select(f => $"{f.Key}={f.Value}"));

    /// <summary>
    /// Carries poll progress across a hot reload. Without this, saving a checklist would reset
    /// every retry count and "last passed at" — so a station that has been failing for ten
    /// minutes would look like it had just started.
    /// <para>
    /// Skipped when the verifier config changed, because then it really is a new question.
    /// Checked state is not carried here: it comes back from the day's saved state.
    /// </para>
    /// </summary>
    public void AdoptRuntimeStateFrom(ChecklistItemViewModel previous)
    {
        if (VerifyFingerprint != previous.VerifyFingerprint)
        {
            return;
        }

        LastResult = previous.LastResult;
        LastDuration = previous.LastDuration;
        LastPassAt = previous.LastPassAt;
        HasLaunched = previous.HasLaunched;
        Attempts = previous.Attempts;
        Status = previous.Status;

        // After Status, whose change handler would otherwise restart the clock.
        FailingSince = previous.FailingSince;
    }

    /// <summary>Flattens this item for the techdesk share.</summary>
    public SnapshotItem ToSnapshotItem() => new()
    {
        Label = Label,
        Tab = TabLabel,
        Type = Item.Type,
        State = IsChecked
            ? SnapshotItemStates.Done
            : Status switch
            {
                VerifyStatus.Failed => SnapshotItemStates.Failing,
                VerifyStatus.Polling => SnapshotItemStates.Polling,
                _ => SnapshotItemStates.Open,
            },
        Detail = string.IsNullOrEmpty(LastResult) ? null : LastResult,
        FailingSince = FailingSince,
        LastPassAt = LastPassAt,
    };

    /// <summary>
    /// Returns the item to its start-of-service condition when the station rolls over to the
    /// next service. Silent on purpose: the rollover is logged once, not once per item, and
    /// twenty CLEARED lines would bury the one entry that explains them.
    /// </summary>
    public void ClearForNewService()
    {
        foreach (var step in SubSteps)
        {
            step.IsDone = false;
        }

        CheckedBy = null;
        CheckedAt = null;
        OverrideNote = null;
        CompletionSource = CompletionSources.Manual;
        HasLaunched = false;
        Attempts = 0;
        LastResult = null;
        LastPassAt = null;
        IsChecked = false;

        // Verified items go straight back to polling so they re-establish themselves; action
        // items wait to be launched again.
        Status = Item.Verify is null || Verifier is null
            ? VerifyStatus.Unknown
            : IsActionType ? VerifyStatus.Unknown : VerifyStatus.Polling;

        OnPropertyChanged(nameof(IsOverridden));
        OnPropertyChanged(nameof(SubLine));
    }

    /// <summary>
    /// Runs this item's verifier once. Called on the UI thread from the poll timer, and the
    /// awaited work returns to the UI thread, so property updates are safe.
    /// </summary>
    public async Task PollAsync(CancellationToken cancellationToken)
    {
        if (Item.Verify is null || Verifier is null || Status == VerifyStatus.Unsupported || IsOverridden)
        {
            return;
        }

        var outcome = await Verifier.CheckAsync(Item.Verify, cancellationToken);

        LastResult = outcome.Result;
        LastDuration = outcome.Duration;

        if (outcome.Passed)
        {
            Attempts = 0;
            LastPassAt = DateTimeOffset.Now;

            if (Status != VerifyStatus.Passed)
            {
                Status = VerifyStatus.Passed;
            }

            if (!IsChecked)
            {
                SetChecked(true, CompletionSources.Auto, null, null);
            }

            return;
        }

        Attempts++;

        if (Status == VerifyStatus.Passed || IsChecked)
        {
            // A verifier that was passing and now is not must un-tick the item and say so.
            // Silently leaving it green is the one behaviour that would make the app lie.
            // Logged once, as FAILED — the un-tick is part of that event, not a second one.
            Status = VerifyStatus.Failed;
            SetChecked(false, CompletionSources.Auto, null, null, log: false);
            _host.ItemChanged(this, LogHow.Failed, $"was passing, now: {outcome.Result}", null);
            return;
        }

        if (IsActionType && !HasLaunched)
        {
            // Not launched yet — this is not a failure, it is a button waiting to be pressed.
            Status = VerifyStatus.Unknown;
            return;
        }

        Status = Attempts >= MaxAttempts ? VerifyStatus.Failed : VerifyStatus.Polling;
    }

    private void SetChecked(bool value, string source, string? initials, string? note, bool log = true)
    {
        if (IsChecked == value && CompletionSource == source)
        {
            return;
        }

        CompletionSource = source;
        CheckedBy = initials;
        OverrideNote = note;
        CheckedAt = value ? DateTimeOffset.Now : null;
        IsChecked = value;

        OnPropertyChanged(nameof(IsOverridden));
        OnPropertyChanged(nameof(SubLine));

        if (!log)
        {
            // Caller is logging this transition itself. State still has to be persisted,
            // so hand it to the host with a null verb rather than skipping the call.
            _host.ItemChanged(this, string.Empty, null, null);
            return;
        }

        var how = source switch
        {
            CompletionSources.Auto => LogHow.Auto,
            CompletionSources.Override => LogHow.Override,
            _ => LogHow.Manual,
        };

        _host.ItemChanged(
            this,
            value ? how : LogHow.Cleared,
            note ?? (value ? null : "unchecked"),
            source == CompletionSources.Auto && value ? LastDuration : null);
    }
}
