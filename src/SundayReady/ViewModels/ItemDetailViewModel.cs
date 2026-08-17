using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SundayReady.ViewModels;

/// <summary>
/// One item, opened up: how to do it, and the steps to tick off while doing it.
/// <para>
/// Distinct from the failed-verify screen, which answers "this broke, what now?". This one
/// answers "how do I do this?" and is available whenever the operator wants it.
/// </para>
/// </summary>
public sealed partial class ItemDetailViewModel : ObservableObject
{
    private readonly Action _close;

    public ItemDetailViewModel(ChecklistItemViewModel item, Action close)
    {
        Item = item;
        _close = close;

        Instructions = item.Item.Instructions
            .Select((text, i) => new CheckStepViewModel(i + 1, text))
            .ToList();
    }

    public ChecklistItemViewModel Item { get; }

    public string Title => Item.Label;

    public string Provenance => $"{Item.TabLabel.ToUpperInvariant()} · {Item.Source.SourceFile.ToUpperInvariant()}";

    public IReadOnlyList<CheckStepViewModel> Instructions { get; }

    public bool HasInstructions => Instructions.Count > 0;

    public bool HasSubSteps => Item.HasSubSteps;

    /// <summary>Says out loud that ticking the last one carries the item, so it is not a surprise.</summary>
    public string SubStepNote =>
        "Ticking all of these ticks the item. You can also tick the item itself if you already know the routine.";

    [RelayCommand]
    private void Close() => _close();
}
