using SundayReady.Models;

namespace SundayReady.Services;

/// <summary>A starting point offered by the setup walkthrough.</summary>
public sealed record ChecklistTemplate(string Key, string Title, string Summary, string[] Sections)
{
    /// <summary>Section label, then its items. Flattened into a definition by <see cref="Build"/>.</summary>
    public required (string Section, string[] Items)[] Content { get; init; }

    public int ItemCount => Content.Sum(g => g.Items.Length);

    public ChecklistDefinition Build(string station, string tabName) => new()
    {
        Station = station,
        Name = tabName,
        Items = Content
            .SelectMany(group => group.Items.Select(label => new ChecklistItem
            {
                Label = label,
                Type = ChecklistItemTypes.Manual,
                Section = string.IsNullOrWhiteSpace(group.Section) ? null : group.Section,
            }))
            .ToList(),
    };
}

/// <summary>
/// Starting points for someone who has just opened the app for the first time.
/// <para>
/// Every item is <c>manual</c>, deliberately. A template that shipped an <c>action</c> pointing at
/// vMix, or a <c>verified</c> item polling a camera, would go red within seconds on a machine where
/// none of that is installed or configured yet — and a brand-new user cannot tell "you have not set
/// this up" apart from "this app is broken". So the walkthrough hands over a list of plain
/// tick-boxes that is immediately correct, and says plainly that items can be upgraded into launch
/// buttons and automatic checks once the paths are known.
/// </para>
/// <para>
/// The wording is a starting point, not a prescription. Every church's booth is different, and the
/// whole point of the editor is that this gets rewritten.
/// </para>
/// </summary>
public static class ChecklistTemplates
{
    public static IReadOnlyList<ChecklistTemplate> All { get; } = new[]
    {
        new ChecklistTemplate(
            "video",
            "Livestream / video",
            "Cameras, the switcher, and getting the stream up.",
            Array.Empty<string>())
        {
            Content = new[]
            {
                ("Before doors", new[]
                {
                    "Cameras powered on and lens caps off",
                    "All camera angles framed and in focus",
                    "Switcher and capture software running",
                    "Recording drive has space free",
                }),
                ("Stream", new[]
                {
                    "Stream created for this service",
                    "Stream key in place and titles match today's service",
                    "Test frame visible on the platform's preview",
                    "Recording armed",
                }),
                ("Final check", new[]
                {
                    "Countdown loop running on the stream output",
                    "Someone has confirmed the stream looks right from outside the building",
                }),
            },
        },

        new ChecklistTemplate(
            "audio",
            "Audio",
            "Console, mics, and the mix that leaves the room.",
            Array.Empty<string>())
        {
            Content = new[]
            {
                ("Before doors", new[]
                {
                    "Console powered on and the right scene loaded",
                    "Fresh batteries in every wireless pack",
                    "Every mic checked and gain set",
                    "Monitors and in-ears working on stage",
                }),
                ("Mix", new[]
                {
                    "House mix set and muted until doors",
                    "Stream mix checked on headphones — not the same as the house mix",
                    "Talkback to the booth working",
                }),
                ("Final check", new[]
                {
                    "Speaker's mic tested by the actual speaker",
                    "Nothing left muted that should not be",
                }),
            },
        },

        new ChecklistTemplate(
            "presentation",
            "Presentation / lyrics",
            "Slides, lyrics, and what the room and the stream each see.",
            Array.Empty<string>())
        {
            Content = new[]
            {
                ("Before doors", new[]
                {
                    "Presentation software running",
                    "This week's service file open and in order",
                    "Screens and stage display showing the right outputs",
                    "Lyrics checked against what the band is actually playing",
                }),
                ("Content", new[]
                {
                    "Sermon slides received and loaded",
                    "Announcement loop up to date",
                    "Any video clips tested with sound",
                }),
                ("Final check", new[]
                {
                    "Blank and logo cues working",
                    "Confidence monitor readable from the platform",
                }),
            },
        },

        new ChecklistTemplate(
            "shutdown",
            "After the service",
            "Packing up. Sits outside the Ready to go gate.",
            Array.Empty<string>())
        {
            Content = new[]
            {
                ("Stop", new[]
                {
                    "Stream ended and recording stopped",
                    "Recording saved somewhere it will be found",
                }),
                ("Pack up", new[]
                {
                    "Wireless packs collected and switched off",
                    "Console faders down and scene saved",
                    "Lens caps on, cameras off",
                }),
                ("Leave it ready", new[]
                {
                    "Anything broken written down for next week",
                    "Booth tidy and locked",
                }),
            },
        },

        new ChecklistTemplate(
            "blank",
            "Start from scratch",
            "One empty item. For a booth that looks like nobody else's.",
            Array.Empty<string>())
        {
            Content = new[]
            {
                (string.Empty, new[] { "First thing to check" }),
            },
        },
    };

    public static ChecklistTemplate? For(string? key) =>
        All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The one template that belongs outside the Ready gate. Kept here rather than as a flag on
    /// the record, because it is the only exception and naming it is clearer than a field that is
    /// false four times out of five.
    /// </summary>
    public static bool IsAfterService(ChecklistTemplate template) =>
        template.Key == "shutdown";
}
