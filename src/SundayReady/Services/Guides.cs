namespace SundayReady.Services;

/// <summary>Plain-language help for one verifier kind.</summary>
public sealed record VerifierGuide(
    string Kind,
    string Headline,
    string What,
    string WhenToUse,
    string Example,
    string? Gotcha = null,
    bool IsStub = false);

/// <summary>A concept worth explaining, for the help window.</summary>
public sealed record Topic(string Title, string Body);

/// <summary>A named group of topics, so the help window is skimmable rather than a wall.</summary>
public sealed record TopicSection(string Title, IReadOnlyList<Topic> Topics);

/// <summary>
/// The one place this is written down. The editor shows a line of it beside the field you are
/// filling in, and the help window shows all of it — so the two can never drift apart.
/// </summary>
public static class Guides
{
    public static IReadOnlyList<VerifierGuide> Verifiers { get; } = new[]
    {
        new VerifierGuide(
            "processRunning",
            "Is a program open?",
            "Looks through the list of running programs for one with this name. Passes the moment it finds it.",
            "Proving an application actually launched — vMix, ProPresenter, a streaming encoder.",
            "processName: vMix64",
            "It matches the program's process name, not its window title. Task Manager → Details shows the real names. The .exe on the end is optional."),

        new VerifierGuide(
            "httpContains",
            "Ask a program's web interface a question",
            "Fetches a web address and passes if the text that comes back contains the words you gave it. Most A/V software has a small web interface for exactly this.",
            "Anything you want to ask software about itself: is vMix up, is a specific camera loaded, is ProPresenter responding.",
            "url: http://127.0.0.1:8088/api   contains: <vmix>",
            "It is a plain text search on the whole response, so pick something that only appears when the thing you care about is true. \"Cam 3\" appears in vMix's reply only when Cam 3 is actually an input."),

        new VerifierGuide(
            "fileExists",
            "Is a file or folder there?",
            "Checks a path on disk. Files and folders both count.",
            "This week's slide folder, a preset file, a recording drive being plugged in.",
            "path: D:\\Services\\Sunday",
            "Environment variables work, so %USERPROFILE%\\Desktop\\... beats hard-coding an operator's name."),

        new VerifierGuide(
            "hostReachable",
            "Is a device on the network?",
            "Pings an address. Give it a port and it connects to that instead, which is the stronger test — something is actually listening.",
            "Cameras, encoders, consoles, NDI boxes. Anything with an address.",
            "host: cam3.local   port: 80",
            "This proves the device is powered and on the network. It says nothing about where a camera is pointed or whether its picture reaches the switcher — for that, ask the switcher with httpContains."),

        new VerifierGuide(
            "ndiSourcePresent",
            "Is an NDI source on the network?",
            "Asks the network which NDI senders are announcing themselves — the same list your switcher shows under Add Input → NDI. Passes when one of them has this text in its name.",
            "Cameras and encoders that reach the switcher over NDI, and the ProPresenter or playback feeds that do the same.",
            "nameContains: CAM-3",
            "Proves the sender is powered, on the network and advertising. It does not prove the switcher has actually taken it as an input, and it will not see sources on another subnet unless you run an NDI Discovery Server. If it fails it lists what it did find, which is usually enough to spot a name that has changed."),

        new VerifierGuide(
            "internetReachable",
            "Is the internet up?",
            "Pings a well-known address, and falls back to an ordinary HTTPS connection if the network blocks ping.",
            "One item near the top of a livestream checklist. It also feeds the connectivity pill in the top bar.",
            "host: 1.1.1.1   (or leave blank)",
            null),

        new VerifierGuide(
            "audioDevicePresent",
            "Is an audio device connected?",
            "Not built yet. An item using this will never pass.",
            "Nothing, for now. Use a manual item and check the meters by eye.",
            "—",
            "Reading Windows audio devices needs work that has not been done, and what to match on depends on how audio reaches the switcher. Do not build an audio checklist around this yet.",
            IsStub: true),
    };

    public static VerifierGuide? For(string? kind) =>
        kind is null ? null : Verifiers.FirstOrDefault(v => string.Equals(v.Kind, kind, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<Topic> ItemTypes { get; } = new[]
    {
        new Topic("manual",
            "A checkbox and nothing else. The operator decides. Use it for anything a person has to look at — "
            + "stage lights, lens caps, a mic that sounds right. Click anywhere on the row to tick it."),

        new Topic("action",
            "A button that runs something: an application, a script, a folder, or a web address. The operator "
            + "presses it, then ticks the row when they are happy. Give it a verifier as well and it ticks "
            + "itself once the verifier agrees, and the button becomes a quiet RELAUNCH chip."),

        new Topic("verified",
            "No button, no tick box. The app checks reality every five seconds and turns the row green on its "
            + "own. If it later stops being true, the row un-ticks itself and says so — the app will not leave "
            + "a green tick on something that has stopped being true."),
    };

    /// <summary>
    /// The topics, grouped and ordered by when somebody needs them.
    /// <para>
    /// Grouping is not decoration. This started as one flat list that grew a topic every time a
    /// feature landed, which put release channels next to what to do about a red item — and the
    /// person opening this window at 10:25 on a Sunday has a problem, not a curiosity. So the
    /// order is: using it, then when it goes wrong, then setting one up, then admin.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TopicSection> Sections { get; } = new[]
    {
        new TopicSection("ON A SUNDAY", new[]
        {
            new Topic("This window, and finding it again",
                "HELP is in the top bar of every screen — the checklist, and the editor too. It is the "
                + "same window from both. Use the search box above rather than reading: type what is going "
                + "wrong or the name of a button, in whatever words come to mind. “red”, “override”, "
                + "“vmix”, “start again” all land somewhere useful. “Show me around”, next to the title, "
                + "walks you round the app itself if the reading is not landing."),

            new Topic("The ring, and what is left",
                "The circle on the right is every item on every tab, not just the one you are looking at. "
                + "Under it is the count, and under that the line that tells you what is still holding the "
                + "gate shut \u2014 \u201c3 items left\u201d. If it says a number you do not expect, another tab has "
                + "something open on it."),

            new Topic("Verified items start checking straight away",
                "This surprises people. A verified item does not wait to be told \u2014 it starts polling the "
                + "moment SundayReady opens. So if vMix is already running when you open the app, the item "
                + "that checks for vMix goes green immediately. That is correct: the item asks \"is this "
                + "true?\", not \"did I make this true?\". An action item with a verifier behaves the same way, "
                + "which is how a station that was already set up comes up mostly green."),

            new Topic("Instructions, and steps to tick off",
                "An item can carry either or both, reached from a chip on its row. \"How to do it\" is "
                + "read-only: numbered instructions for the job someone does four times a year and cannot be "
                + "expected to remember \u2014 setting up the stream in Subsplash, say. \"Steps to tick off\" are "
                + "ticked individually, remembered with the rest of the day, and finishing the last one ticks "
                + "the item itself; the item can still be ticked directly by anyone who knows the routine. "
                + "Neither is the same as \"check these, in order\", which only appears when a verifier has "
                + "failed \u2014 that is diagnosis, these are the work."),

            new Topic("Quick launch",
                "The buttons at the bottom of the right-hand rail open the software this station uses, "
                + "without hunting for it on the desktop. They are not checklist items and nothing depends "
                + "on them \u2014 they are there because the alternative is minimising the app. Set them up in "
                + "Settings \u2192 Quick launch."),

            new Topic("Ready to go, and what happens after it",
                "The gate at the bottom of the rail. It stays inert until every item on every tab is ticked "
                + "or overridden \u2014 not per tab, the whole station. A checklist with \"must be finished before "
                + "the station counts as ready\" unticked stays out of it, which is how a post-show or "
                + "shutdown list can exist without holding the gate shut.\n\n"
                + "Pressing it is the operator saying setup is finished. It goes in the completion log, and "
                + "the checklist then gets out of the way: the screen becomes a count-up into the service "
                + "with the clock under it, the live viewer counts, and a panel naming anything that has "
                + "stopped passing since you went ready. The verifiers keep running the whole time \u2014 during "
                + "a service what matters is not the list but whether something that was true has stopped "
                + "being true. \u201cShow the checklist\u201d brings it back at any point. When the service is over, "
                + "\u201cService finished\u201d opens whichever checklist you ticked \u201copen this checklist after the "
                + "service\u201d on in the editor; without one, nothing has been nominated and the button says so "
                + "rather than appearing to do nothing."),

            new Topic("Signing off, and the log",
                "Going ready asks who you are and records it. Every station writes one file per day to its "
                + "logs folder, append-only: what was ticked, what ticked itself, anything overridden and "
                + "why, and when the operator signed off. LOG in the top bar opens it. It exists so that "
                + "\u201cwhat happened last week\u201d is a question with an answer, not a memory test."),

            new Topic("Sections",
                "A heading above a run of items. Type the same section name on consecutive items and they "
                + "group under one divider \u2014 BOOT \u00b7 30 MIN BEFORE, CAMERAS & CAPTURE, whatever suits. Purely "
                + "for reading; it changes nothing about how items behave."),
        }),

        new TopicSection("WHEN SOMETHING WILL NOT PASS", new[]
        {
            new Topic("A red item, and what to do first",
                "Red means a check the app can make has failed enough times to stop being hopeful. Open the "
                + "row: it names what it looked for and what it got, and shows whatever troubleshooting steps "
                + "your church wrote for that item. Work down them. \u201cRetry now\u201d re-checks immediately rather "
                + "than waiting for the next poll, so you get an answer as soon as you have changed "
                + "something. Most red items on a Sunday are something not switched on yet."),

            new Topic("Check these, in order",
                "Troubleshooting steps you write yourself, shown when an item fails. This is the most "
                + "valuable thing in a checklist and the app cannot write it for you: it is what you would "
                + "tell a volunteer over the phone. \"Is the PoE injector lit? It's the grey box behind the "
                + "booth.\""),

            new Topic("Failed polls before it goes red",
                "Software takes time to start. This is how many failed checks to treat as \"still starting\" "
                + "before the row turns red. The default is 10, which at one check every five seconds is "
                + "about a minute. Lower it for something that should already be true \u2014 a camera that is "
                + "either on the network or is not. Raise it for something slow to load."),

            new Topic("Overriding a failing item",
                "When a verifier is red and you are out of time, Override & note ticks the item anyway. It "
                + "asks for initials and a typed reason, both required, and writes them to the completion "
                + "log. The service is then recorded as partial. It is an honest escape hatch, not a way to "
                + "silence a check."),

            new Topic("When you genuinely cannot fix it",
                "Override it with a real reason, say what you tried, and go on with the service \u2014 the "
                + "checklist is there to make sure nothing is forgotten, not to stop the service starting. "
                + "The note you type is what somebody reads on Monday, so \u201ccamera 3 dead, no picture, tried "
                + "reseating the PoE\u201d is worth far more than \u201cbroken\u201d. If the room has a techdesk it can "
                + "already see your station is not ready, and its Page button is how it reaches you."),
        }),

        new TopicSection("SETTING A STATION UP", new[]
        {
            new Topic("The system map",
                "MAP in the top bar opens a live picture of the building: your gear as boxes, the "
                + "wires between them drawn in the signal's colour, moving while the signal is "
                + "believed to be arriving. Boxes and wires can each carry the same checks as "
                + "checklist items \u2014 which is the part a checklist cannot do: a camera that is "
                + "powered and a switcher that is running still tell you nothing about whether the "
                + "camera reaches the switcher. The one red pulsing wire is the thing that actually "
                + "broke; paths it starves go faint and still instead. Maps are shared \u2014 every "
                + "station reads the same folder as the techdesk \u2014 so the building is drawn once."),

            new Topic("Editing the map",
                "Press Edit layout. Add device drops a box; drag boxes where they make sense; "
                + "select a box then Draw connection and click what it feeds. Selecting anything "
                + "while editing opens its editor in the rail: name, the mono sub-line, how the app "
                + "knows its state, and a verifier \u2014 the same kinds the checklist uses. A box can "
                + "also open another map, which is how one tidy overview drills into the messy "
                + "details. Apply changes updates the live map; Save map writes the file. Nothing "
                + "on a map can hold Ready to go unless it is verified and on campus \u2014 a hollow "
                + "box is a guess, and the app never lets a guess look like a check."),

            new Topic("The setup walkthrough",
                "A machine that has never run SundayReady opens a short walkthrough instead of an empty "
                + "list: name the station, say when the services are, and pick or create a first checklist. "
                + "It writes an ordinary station.json and ordinary checklist files \u2014 there is no special "
                + "mode, and everything it sets is in Settings and the editor afterwards. It can be skipped "
                + "from any screen, and run again from Settings \u2192 Identity, which is the quickest way to set "
                + "up a station being repurposed. Re-running it never overwrites an existing checklist; a "
                + "name that collides gets a numbered suffix."),

            new Topic("The guided tour",
                "\u201cShow me around\u201d, at the top of this window, dims the app and walks you round the real "
                + "controls one at a time \u2014 the tabs, the list, the Ready to go gate \u2014 and then has you "
                + "actually open the editor, add an item and save it. Ten stops, with Skip tour pinned at the "
                + "top throughout. It is a different thing from the setup walkthrough: that one configures a "
                + "station without ever showing you the app, and this one shows you the app without changing "
                + "anything you have not chosen to change."),

            new Topic("Why a new checklist is all tick-boxes",
                "The templates the walkthrough offers contain nothing but manual items, deliberately. An "
                + "item that launches vMix needs a path that is right for this building, and one that checks "
                + "a camera needs its address \u2014 shipped as guesses, they would go red within seconds on a "
                + "machine where none of it is set up, and a new user cannot tell that apart from a broken "
                + "app. So you start with a list that is honestly correct, then upgrade the items worth "
                + "automating in the editor."),

            new Topic("When the checklist starts again",
                "By default, every time SundayReady opens. If you also set service times, it starts again at "
                + "each changeover \u2014 with services at 09:00 and 11:00 and a 90 minute lead, the list goes "
                + "fresh at 09:30 for the second one. A new calendar day always clears it."),

            new Topic("The techdesk",
                "The same program in a different mode, for a screen that watches every station at once "
                + "instead of running one. Stations publish a snapshot to a shared folder every few seconds "
                + "and the techdesk reads all of them, so it needs no connection to the machines themselves. "
                + "Turn it on in Settings \u2192 Techdesk mode; it takes effect at the next restart."),
        }),

        new TopicSection("KEEPING IT UP TO DATE", new[]
        {
            new Topic("Installing an update now",
                "An automatic update downloads in the background and waits: it is swapped in the next time "
                + "this station starts SundayReady, so nothing ever changes under an operator mid-service. "
                + "When one is waiting, Settings \u2192 Updates offers to install it and restart straight away "
                + "\u2014 which is safe precisely because somebody asked for it. The app closes, comes back a "
                + "couple of seconds later on the new build, and the checklist is where it was."),

            new Topic("Update channels",
                "A channel is how far ahead of finished this station is willing to run, not a separate "
                + "version of the app. Production takes only finished releases and is where anything that "
                + "runs a service belongs. Beta, alpha and dev each take everything above them as well, so a "
                + "station on beta still gets production releases \u2014 it just gets the betas first. Put the "
                + "spare machine on beta and let it find the problems. Updates never go backwards, so coming "
                + "back from dev to production means installing the production build by hand from the "
                + "releases page. One catch: following a channel needs 0.15.0 or later. A station on an "
                + "older build can only see finished releases, whatever its setting says, until it has "
                + "picked one up."),
        }),
    };

    /// <summary>Every topic, flattened. What the search box filters over.</summary>
    public static IReadOnlyList<Topic> Concepts { get; } =
        Sections.SelectMany(section => section.Topics).ToList();

}
