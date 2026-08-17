using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SundayReady.Services;

namespace SundayReady.ViewModels;

/// <summary>
/// Position in the guided tour. Deliberately knows nothing about windows or controls — the host
/// watches this and moves the spotlight, so the tour can cross from the station to the editor and
/// back without this class caring that it did.
/// </summary>
public sealed partial class TourViewModel : ObservableObject
{
    private readonly IReadOnlyList<TourStep> _steps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Step), nameof(Title), nameof(Body), nameof(Prompt),
        nameof(HasPrompt), nameof(CounterLabel), nameof(CanGoBack), nameof(IsLast),
        nameof(WaitsForClick), nameof(NextLabel))]
    private int _index;

    [ObservableProperty]
    private bool _isRunning = true;

    public TourViewModel(IReadOnlyList<TourStep>? steps = null)
    {
        _steps = steps ?? Tour.Steps;
    }

    public TourStep Step => _steps[Index];

    public int Count => _steps.Count;

    public string Title => Step.Title;

    public string Body => Step.Body;

    public string? Prompt => Step.Prompt;

    public bool HasPrompt => !string.IsNullOrEmpty(Step.Prompt);

    public string CounterLabel => $"{Index + 1} OF {Count}";

    public bool CanGoBack => Index > 0;

    public bool IsLast => Index == Count - 1;

    /// <summary>True when the step is waiting for the person to press the real control.</summary>
    public bool WaitsForClick => Step.Advance == TourAdvance.Click;

    public string NextLabel => IsLast ? "Done" : "Next";

    /// <summary>Raised when the tour ends, whether it was finished or skipped.</summary>
    public event EventHandler? Ended;

    /// <summary>Raised when the step changes, so the host can move the spotlight.</summary>
    public event EventHandler? Moved;

    [RelayCommand]
    public void Next()
    {
        if (IsLast)
        {
            End();
            return;
        }

        Index++;
        Moved?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Back()
    {
        if (Index == 0)
        {
            return;
        }

        Index--;
        Moved?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Skip() => End();

    /// <summary>
    /// The person pressed the control the current step points at. Only advances on a step that
    /// was waiting for it, so a stray click on a highlighted button does not skip ahead.
    /// </summary>
    public void TargetClicked()
    {
        if (IsRunning && WaitsForClick)
        {
            Next();
        }
    }

    /// <summary>
    /// Skips past any step whose control is not on screen — an empty station has no item rows to
    /// point at, and a tour that spotlights nothing is worse than one that is a step shorter.
    /// Returns false when there is nothing left to show.
    /// </summary>
    public bool SkipWhile(Func<TourStep, bool> missing)
    {
        while (IsRunning && missing(Step))
        {
            if (IsLast)
            {
                End();
                return false;
            }

            Index++;
        }

        return IsRunning;
    }

    private void End()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;
        Ended?.Invoke(this, EventArgs.Empty);
    }
}
