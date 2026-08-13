using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SundayReady.Models;

namespace SundayReady.ViewModels;

/// <summary>A run of items under one divider label. A grouping in the data, not an item type.</summary>
public sealed class ChecklistSectionViewModel
{
    public ChecklistSectionViewModel(string? label, IEnumerable<ChecklistItemViewModel> items)
    {
        Label = label?.ToUpperInvariant() ?? string.Empty;
        Items = new ObservableCollection<ChecklistItemViewModel>(items);
    }

    public string Label { get; }

    public bool HasLabel => !string.IsNullOrEmpty(Label);

    public ObservableCollection<ChecklistItemViewModel> Items { get; }
}

/// <summary>One checklist file, rendered as one tab.</summary>
public sealed partial class ChecklistTabViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(IsComplete), nameof(IsUntouched))]
    private int _completedCount;

    public ChecklistTabViewModel(ChecklistDefinition definition, IReadOnlyList<ChecklistItemViewModel> items)
    {
        Definition = definition;
        Items = new ObservableCollection<ChecklistItemViewModel>(items);

        // Preserve authored order; group runs of the same section rather than sorting, so a
        // checklist reads in the order someone wrote it.
        var sections = new List<ChecklistSectionViewModel>();
        var index = 0;
        while (index < items.Count)
        {
            var label = items[index].Item.Section;
            var run = new List<ChecklistItemViewModel>();

            while (index < items.Count && string.Equals(items[index].Item.Section, label, StringComparison.OrdinalIgnoreCase))
            {
                run.Add(items[index]);
                index++;
            }

            sections.Add(new ChecklistSectionViewModel(label, run));
        }

        Sections = new ObservableCollection<ChecklistSectionViewModel>(sections);
        _completedCount = items.Count(i => i.IsChecked);
    }

    public ChecklistDefinition Definition { get; }

    public ObservableCollection<ChecklistItemViewModel> Items { get; }

    public ObservableCollection<ChecklistSectionViewModel> Sections { get; }

    public string Label => Definition.TabLabel;

    public int TotalCount => Items.Count;

    public int FailingCount => Items.Count(i => i.IsFailing);

    public string CountText => $"{CompletedCount}/{TotalCount}";

    public bool IsComplete => TotalCount > 0 && CompletedCount == TotalCount;

    public bool IsUntouched => CompletedCount == 0;

    /// <summary>Set by the station. Keeps the tab template free of parent-lookup bindings.</summary>
    public Action<ChecklistTabViewModel>? Selecting { get; set; }

    [RelayCommand]
    private void Select() => Selecting?.Invoke(this);

    public void Refresh()
    {
        CompletedCount = Items.Count(i => i.IsChecked);
        OnPropertyChanged(nameof(FailingCount));
    }
}
