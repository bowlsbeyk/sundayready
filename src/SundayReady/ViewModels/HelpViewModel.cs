using SundayReady.Services;

namespace SundayReady.ViewModels;

/// <summary>
/// The help window. Read-only — everything comes from <see cref="Guides"/>, which the
/// checklist editor also draws its inline hints from, so the two cannot disagree.
/// </summary>
public sealed class HelpViewModel
{
    public IReadOnlyList<VerifierGuide> Verifiers => Guides.Verifiers;

    public IReadOnlyList<Topic> ItemTypes => Guides.ItemTypes;

    public IReadOnlyList<Topic> Concepts => Guides.Concepts;

    public string Intro =>
        "A checklist is a list of items. Each item is either something a person confirms, a button that "
        + "launches something, or something the app checks for itself. The rest of this explains what you "
        + "can put in each of those, and the handful of ideas worth knowing.";

    public string Version => $"SundayReady {AppVersion.Display}";
}
