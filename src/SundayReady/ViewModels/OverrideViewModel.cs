using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SundayReady.ViewModels;

/// <summary>
/// An override is a person taking responsibility, so it takes initials and a typed reason.
/// The handoff is explicit that overrides without a note should not be possible — hence the
/// commands staying disabled rather than defaulting the note to something bland.
/// </summary>
public sealed partial class OverrideViewModel : ObservableObject
{
    private readonly Action _close;
    private readonly Action<string, string> _apply;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _initials = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _note = string.Empty;

    public OverrideViewModel(ChecklistItemViewModel item, string? knownInitials, Action<string, string> apply, Action close)
    {
        Item = item;
        _apply = apply;
        _close = close;
        _initials = knownInitials ?? string.Empty;
    }

    public ChecklistItemViewModel Item { get; }

    public string Title => Item.Label;

    public string Explanation =>
        "This marks the item done without the verifier agreeing. It goes in the completion log "
        + "with your initials, and the service is recorded as partial.";

    private bool CanConfirm => !string.IsNullOrWhiteSpace(Initials) && !string.IsNullOrWhiteSpace(Note.Trim());

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        _apply(Initials.Trim().ToUpperInvariant(), Note.Trim());
        _close();
    }

    [RelayCommand]
    private void Cancel() => _close();
}
