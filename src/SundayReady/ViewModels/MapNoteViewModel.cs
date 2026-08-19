using CommunityToolkit.Mvvm.ComponentModel;
using SundayReady.Models;

namespace SundayReady.ViewModels;

/// <summary>
/// A note pinned to the canvas.
/// <para>
/// Notes exist because a map answers "how is this wired" and people keep needing to write down
/// "…and here is the thing that will bite you". The spare cable lives in the drawer under the amp.
/// This run goes through the ceiling and pulling it will bring the tile down. That XLR is
/// intermittent — wiggle it before blaming the desk. None of that fits in a box or a line, and all
/// of it is what somebody at 8am on a Sunday actually needs.
/// </para>
/// <para>
/// Deliberately outside the health model. A note is never checked, never rolls up, and never
/// changes whether the system reads green — the moment a note could move the verdict, notes start
/// getting written to move the verdict.
/// </para>
/// </summary>
public sealed partial class MapNoteViewModel : ObservableObject
{
    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isDimmed;

    /// <summary>
    /// The note's text box is only a text box while somebody is actually typing into it.
    /// The rest of the time it reads as what it is — a note — because a permanently exposed
    /// input field looks unsaved forever, and "how do I save this?" is the question a note
    /// must never raise.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsText))]
    private bool _isTextEditing;

    /// <summary>The read view: the text, or a nudge when there is none yet.</summary>
    public string DisplayText => string.IsNullOrWhiteSpace(Text)
        ? "Double-click to write…"
        : Text;

    public bool IsPlaceholder => string.IsNullOrWhiteSpace(Text);

    public bool ShowsText => !IsTextEditing;

    /// <summary>Leaves typing mode and pushes the text into the model.</summary>
    public void EndTextEditing()
    {
        if (IsTextEditing)
        {
            IsTextEditing = false;
            Commit();
        }
    }

    public MapNoteViewModel(MapNote model, MapDeviceViewModel? about)
    {
        Model = model;
        About = about;
        _x = model.X;
        _y = model.Y;
        _text = model.Text;

        if (about is not null)
        {
            // An attached note travels with its device. A warning about the left XLR that drifts
            // three boxes away while somebody rearranges the map is worse than no warning: it is
            // a warning about the wrong thing.
            about.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(MapDeviceViewModel.X) or nameof(MapDeviceViewModel.Y))
                {
                    OnPropertyChanged(nameof(HasTether));
                    OnPropertyChanged(nameof(TetherX));
                    OnPropertyChanged(nameof(TetherY));
                }
            };
        }
    }

    public MapNote Model { get; }

    /// <summary>The device this note is about, when it names one.</summary>
    public MapDeviceViewModel? About { get; }

    public string Id => Model.Id;

    public bool IsWarning => Model.Tone == MapNoteTones.Warning;

    /// <summary>The tone is stored on the model, so a change there has to be announced by hand.</summary>
    public void OnToneChanged() => OnPropertyChanged(nameof(IsWarning));

    public string? Author => Model.Author;

    public bool HasAuthor => !string.IsNullOrWhiteSpace(Model.Author);

    /// <summary>Notes are a fixed width so a wall of them stays a grid rather than a collage.</summary>
    public const double NoteWidth = 210;

    public bool HasTether => About is not null;

    /// <summary>Where the tether line ends, relative to the note's own top-left corner.</summary>
    public double TetherX => About is null ? 0 : About.Centre.X - X;

    public double TetherY => About is null ? 0 : About.Centre.Y - Y;

    /// <summary>Pushes the live position back into the model, before a save.</summary>
    public void Commit()
    {
        Model.X = Math.Round(X);
        Model.Y = Math.Round(Y);
        Model.Text = Text;
    }

    partial void OnXChanged(double value)
    {
        Model.X = value;
        OnPropertyChanged(nameof(TetherX));
    }

    partial void OnYChanged(double value)
    {
        Model.Y = value;
        OnPropertyChanged(nameof(TetherY));
    }

    partial void OnTextChanged(string value)
    {
        Model.Text = value;
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(IsPlaceholder));
    }
}
