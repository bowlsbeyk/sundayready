using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SundayReady.ViewModels;

public sealed class CheckStepViewModel
{
    public CheckStepViewModel(int number, string text)
    {
        Number = number.ToString();
        Text = text;
    }

    public string Number { get; }

    public string Text { get; }
}

/// <summary>
/// Turns a red row into something a volunteer can act on. Everything here is either the real
/// verifier spec or troubleshooting copy authored per item — nothing is generated prose.
/// </summary>
public sealed partial class FailedVerifyViewModel : ObservableObject
{
    private readonly Action _close;

    public FailedVerifyViewModel(ChecklistItemViewModel item, int itemNumber, Action close)
    {
        Item = item;
        _close = close;

        Provenance = string.Join(" · ",
            (item.IsVerifiedType ? "VERIFIED ITEM" : "ACTION ITEM"),
            item.Source.SourceFile.ToUpperInvariant(),
            $"ITEM {itemNumber}");

        var fields = item.Item.Verify?.DescribeFields().ToList() ?? new List<KeyValuePair<string, string>>();
        var width = fields.Count == 0 ? 0 : fields.Max(f => f.Key.Length) + 2;

        SpecLines = fields.Select(f => $"{f.Key.PadRight(width)}{f.Value}").ToList();
        ResultLine = $"{"result".PadRight(width)}{item.LastResult}";

        Steps = item.Item.CheckSteps
            .Select((text, i) => new CheckStepViewModel(i + 1, text))
            .ToList();

        HasSteps = Steps.Count > 0;
        HasRemediation = item.Item.Remediation is not null;
        RemediationLabel = item.Item.RemediationLabel ?? "Run fix";
    }

    public ChecklistItemViewModel Item { get; }

    public string Title => Item.Label;

    public string Provenance { get; }

    public IReadOnlyList<string> SpecLines { get; }

    public string ResultLine { get; }

    public IReadOnlyList<CheckStepViewModel> Steps { get; }

    public bool HasSteps { get; }

    public bool HasRemediation { get; }

    public string RemediationLabel { get; }

    [RelayCommand]
    private void RetryNow()
    {
        Item.RetryNowCommand.Execute(null);
        _close();
    }

    [RelayCommand]
    private void Remediate() => Item.RemediateCommand.Execute(null);

    [RelayCommand]
    private void Override()
    {
        _close();
        Item.OverrideCommand.Execute(null);
    }

    [RelayCommand]
    private void Close() => _close();
}
