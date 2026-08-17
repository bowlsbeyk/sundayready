using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SundayReady.Services;

namespace SundayReady.ViewModels;

/// <summary>One group of topics as the help window shows it, after filtering.</summary>
public sealed class HelpSectionViewModel
{
    public HelpSectionViewModel(string title, IReadOnlyList<Topic> topics)
    {
        Title = title;
        Topics = topics;
    }

    public string Title { get; }

    public IReadOnlyList<Topic> Topics { get; }
}

/// <summary>
/// The help window. Read-only — everything comes from <see cref="Guides"/>, which the checklist
/// editor also draws its inline hints from, so the two cannot disagree.
/// <para>
/// The search box is the point of this class. Twenty-odd entries in one scroll is a reference, and
/// somebody opens this window because something is wrong with ninety seconds to go, not to read.
/// Filtering runs over titles and bodies together, so typing "red" or "override" or "vmix" gets
/// there without knowing which heading it was filed under.
/// </para>
/// </summary>
public sealed partial class HelpViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuery), nameof(ResultLabel), nameof(HasNoResults))]
    private string _query = string.Empty;

    public HelpViewModel()
    {
        Apply();
    }

    public ObservableCollection<VerifierGuide> Verifiers { get; } = new();

    public ObservableCollection<Topic> ItemTypes { get; } = new();

    public ObservableCollection<HelpSectionViewModel> Sections { get; } = new();

    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);

    public bool ShowIntro => !HasQuery;

    public bool HasItemTypes => ItemTypes.Count > 0;

    public bool HasVerifiers => Verifiers.Count > 0;

    public bool HasNoResults => HasQuery && Matches == 0;

    public string ResultLabel => Matches == 1 ? "1 match" : $"{Matches} matches";

    private int Matches => ItemTypes.Count + Verifiers.Count + Sections.Sum(s => s.Topics.Count);

    public string Intro =>
        "A checklist is a list of items. Each item is either something a person confirms, a button that "
        + "launches something, or something the app checks for itself. The rest of this explains what you "
        + "can put in each of those, and the handful of ideas worth knowing.";

    public string Version => $"SundayReady {AppVersion.Display}";

    [RelayCommand]
    private void ClearQuery() => Query = string.Empty;

    partial void OnQueryChanged(string value) => Apply();

    private void Apply()
    {
        var terms = Query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        ItemTypes.Clear();
        foreach (var topic in Guides.ItemTypes.Where(t => IsMatch(terms, t.Title, t.Body)))
        {
            ItemTypes.Add(topic);
        }

        Verifiers.Clear();
        foreach (var guide in Guides.Verifiers.Where(g =>
                     IsMatch(terms, g.Kind, g.Headline, g.What, g.WhenToUse, g.Example, g.Gotcha)))
        {
            Verifiers.Add(guide);
        }

        Sections.Clear();

        var matched = new List<(HelpSectionViewModel Section, bool ByTitle)>();
        foreach (var section in Guides.Sections)
        {
            var topics = section.Topics.Where(t => IsMatch(terms, t.Title, t.Body)).ToList();
            if (topics.Count == 0)
            {
                continue;
            }

            // A title match is a much stronger signal than a word buried in a body. Searching
            // "red" should lead with "A red item, and what to do first", not with whichever
            // section happens to come first and mentions the word in passing.
            topics = topics
                .OrderBy(t => IsMatch(terms, t.Title) ? 0 : 1)
                .ToList();

            matched.Add((
                new HelpSectionViewModel(section.Title, topics),
                topics.Any(t => IsMatch(terms, t.Title))));
        }

        // Sections keep their reading order when nothing is being searched for.
        foreach (var (section, _) in matched.OrderBy(m => m.ByTitle ? 0 : 1))
        {
            Sections.Add(section);
        }

        OnPropertyChanged(nameof(ShowIntro));
        OnPropertyChanged(nameof(HasItemTypes));
        OnPropertyChanged(nameof(HasVerifiers));
        OnPropertyChanged(nameof(HasNoResults));
        OnPropertyChanged(nameof(ResultLabel));
    }

    /// <summary>
    /// Every word has to appear somewhere in the entry, in any of its fields. Deliberately not a
    /// phrase match: "vmix red" should find the vMix entry and the red-item entry rather than
    /// nothing, because that is how somebody types when they are in a hurry.
    /// </summary>
    private static bool IsMatch(IReadOnlyList<string> terms, params string?[] fields)
    {
        if (terms.Count == 0)
        {
            return true;
        }

        var haystack = string.Join(' ', fields.Where(f => !string.IsNullOrEmpty(f)));
        return terms.All(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
