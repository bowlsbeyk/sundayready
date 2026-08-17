namespace SundayReady.Services;

/// <summary>Which window a tour step points at.</summary>
public enum TourSurface
{
    Station,
    Editor,
}

/// <summary>How a step is finished.</summary>
public enum TourAdvance
{
    /// <summary>The person reads it and presses Next.</summary>
    Next,

    /// <summary>
    /// The person actually clicks the thing being pointed at. Used where doing it is the lesson
    /// — pressing EDIT, adding an item, saving — because being told where a button is and
    /// pressing it are different amounts of learning.
    /// </summary>
    Click,
}

/// <summary>Where a callout sits relative to the control it points at.</summary>
public enum TourPlacement
{
    Below,
    Above,
    Left,
    Right,
}

/// <summary>
/// One stop on the tour. <paramref name="Target"/> is the <c>x:Name</c> of a control in the
/// window named by <paramref name="Surface"/>; a step whose target cannot be found is skipped
/// rather than shown pointing at nothing.
/// </summary>
public sealed record TourStep(
    TourSurface Surface,
    string Target,
    string Title,
    string Body,
    TourPlacement Placement = TourPlacement.Below,
    TourAdvance Advance = TourAdvance.Next)
{
    /// <summary>Shown under the body when the step is waiting for a real click.</summary>
    public string? Prompt { get; init; }
}

/// <summary>
/// The guided tour: a spotlight over the real interface that ends with the person having
/// actually built a checklist item.
/// <para>
/// It exists alongside the setup walkthrough rather than replacing it, because the two teach
/// different things. The walkthrough gets a station configured and never shows you the app. This
/// points at the real controls in the real windows, so what you learn is where things are —
/// which is the part that has to survive until next Sunday.
/// </para>
/// </summary>
public static class Tour
{
    public static IReadOnlyList<TourStep> Steps { get; } = new[]
    {
        new TourStep(
            TourSurface.Station,
            "TourTabs",
            "One tab per checklist",
            "Each file is a tab, and the number beside it is how many of its items are done. A "
            + "station can have as many as it needs — video, audio, packing up afterwards.",
            TourPlacement.Below),

        new TourStep(
            TourSurface.Station,
            "TourList",
            "The list itself",
            "Click a row to tick it. Items the app checks for itself tick without being asked, "
            + "and one that fails turns red and offers you the reason, a retry, and a way to "
            + "override it if you know better.",
            TourPlacement.Right),

        new TourStep(
            TourSurface.Station,
            "TourReady",
            "Ready to go",
            "Stays locked until every item on every tab is done. That button is the whole point "
            + "of the app — it is how this station says out loud that it is ready.",
            TourPlacement.Left),

        new TourStep(
            TourSurface.Station,
            "TourEdit",
            "Let's build something",
            "EDIT is where checklists are written. Everything you see on the left came from a "
            + "file you can change here.",
            TourPlacement.Below,
            TourAdvance.Click)
        {
            Prompt = "Press EDIT to carry on.",
        },

        new TourStep(
            TourSurface.Editor,
            "TourFiles",
            "Your checklists",
            "One row per file. The tick decides whether this station shows it as a tab, so a "
            + "checklist can exist without being on screen — handy while you are writing one.",
            TourPlacement.Right),

        new TourStep(
            TourSurface.Editor,
            "TourAddItem",
            "Add an item",
            "This adds a new row to whichever checklist is selected. Go on — it is your station, "
            + "and nothing is written to disk until you save.",
            TourPlacement.Above,
            TourAdvance.Click)
        {
            Prompt = "Press Add item.",
        },

        new TourStep(
            TourSurface.Editor,
            "TourItemLabel",
            "Say what has to be true",
            "Write it the way you would say it to whoever is standing at this desk: “Lens caps "
            + "off”, “Stream key pasted into vMix”. Short, and checkable at a glance.",
            TourPlacement.Left),

        new TourStep(
            TourSurface.Editor,
            "TourItemType",
            "Three kinds of item",
            "Manual is a tick-box. Action gives the row a button that launches software. Verified "
            + "is checked by the app itself — a process running, an API answering — and ticks on "
            + "its own. Start with manual; upgrade the ones worth automating later.",
            TourPlacement.Left),

        new TourStep(
            TourSurface.Editor,
            "TourSave",
            "Save it",
            "This writes the file. The checklist behind this window is watching the folder, so it "
            + "updates the moment the save lands — no restart, no reload.",
            TourPlacement.Above,
            TourAdvance.Click)
        {
            Prompt = "Press Save, then close this window.",
        },

        new TourStep(
            TourSurface.Station,
            "TourHelp",
            "And when you get stuck: HELP",
            "Your item is in the list behind this. Everything else lives behind HELP, in the top "
            + "bar, on every screen — including the editor. It has a search box, so on a Sunday "
            + "morning you can type “red” or “override” and get the answer rather than reading.\n\n"
            + "Open it now, so you know where it is. That is the end of the tour.",
            TourPlacement.Below,
            TourAdvance.Click)
        {
            Prompt = "Press HELP. You can reopen it any time — it is always there.",
        },
    };
}
