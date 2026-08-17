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
            "nameContains: BALCONY-CAM",
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

    public static IReadOnlyList<Topic> Concepts { get; } = new[]
    {
        new Topic("Verified items start checking straight away",
            "This surprises people. A verified item does not wait to be told — it starts polling the moment "
            + "SundayReady opens. So if vMix is already running when you open the app, the item that checks "
            + "for vMix goes green immediately. That is correct: the item asks \"is this true?\", not \"did I "
            + "make this true?\". An action item with a verifier behaves the same way, which is how a station "
            + "that was already set up comes up mostly green."),

        new Topic("Failed polls before it goes red",
            "Software takes time to start. This is how many failed checks to treat as \"still starting\" before "
            + "the row turns red. The default is 10, which at one check every five seconds is about a minute. "
            + "Lower it for something that should already be true — a camera that is either on the network or "
            + "is not. Raise it for something slow to load."),

        new Topic("Sections",
            "A heading above a run of items. Type the same section name on consecutive items and they group "
            + "under one divider — BOOT · 30 MIN BEFORE, CAMERAS & CAPTURE, whatever suits. Purely for reading; "
            + "it changes nothing about how items behave."),

        new Topic("Ready to go",
            "The gate at the bottom of the right-hand rail. It stays inert until every item on every tab is "
            + "ticked or overridden — not per tab, the whole station. A checklist with \"must be finished "
            + "before the station counts as ready\" unticked stays out of it, which is how a post-show or "
            + "shutdown list can exist without holding the gate shut."),

        new Topic("Instructions, and steps to tick off",
            "An item can carry either or both, reached from a chip on its row. \"How to do it\" is "
            + "read-only: numbered instructions for the job someone does four times a year and cannot be "
            + "expected to remember — setting up the stream in Subsplash, say. \"Steps to tick off\" are "
            + "ticked individually, remembered with the rest of the day, and finishing the last one ticks the "
            + "item itself; the item can still be ticked directly by anyone who knows the routine. "
            + "Neither is the same as \"check these, in order\", which only appears when a verifier has "
            + "failed — that is diagnosis, these are the work."),

        new Topic("Ready to go, and what happens after it",
            "Pressing it is the operator saying setup is finished. It is written to the completion log, and "
            + "the checklist then gets out of the way: the screen becomes a count-up into the service with the "
            + "clock under it, the live viewer counts, and a panel that names anything which has stopped "
            + "passing since you went ready. The verifiers keep running the whole time — during a service what "
            + "matters is not the list but whether something that was true has stopped being true. "
            + "\"Show the checklist\" brings it back at any point. When the service is over, \"Service finished\" "
            + "moves you on and opens whichever checklist you ticked \"open this checklist after the service\" "
            + "on in the editor. Without one, nothing has been nominated and the button says so rather than "
            + "appearing to do nothing."),

        new Topic("Overriding a failing item",
            "When a verifier is red and you are out of time, Override & note ticks the item anyway. It asks "
            + "for initials and a typed reason, both required, and writes them to the completion log. The "
            + "service is then recorded as partial. It is an honest escape hatch, not a way to silence a check."),

        new Topic("Check these, in order",
            "Troubleshooting steps you write yourself, shown when an item fails. This is the most valuable "
            + "thing in a checklist and the app cannot write it for you: it is what you would tell a volunteer "
            + "over the phone. \"Is the PoE injector lit? It's the grey box behind the booth.\""),

        new Topic("When the checklist starts again",
            "By default, every time SundayReady opens. If you also set service times, it starts again at each "
            + "changeover — with services at 09:00 and 11:00 and a 90 minute lead, the list goes fresh at "
            + "09:30 for the second one. A new calendar day always clears it."),
    };
}
