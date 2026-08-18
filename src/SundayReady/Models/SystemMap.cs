using System.Text.Json.Serialization;

namespace SundayReady.Models;

/// <summary>
/// One picture of how the building is wired: devices and the connections between them.
/// <para>
/// A map is a diagnostic layer, not a procedural one. A checklist asks "has somebody done the
/// things?"; a map asks "is the signal path intact right now?" — and it can answer the question a
/// checklist structurally cannot, which is <em>where</em> a path broke. A camera that is powered
/// and a switcher that is running still tell you nothing about whether the camera reaches the
/// switcher, so a connection is verifiable in its own right.
/// </para>
/// <para>
/// The shape follows the design handoff's data model. Two of its rules are enforced in the model
/// rather than the view, because they are honesty rules, not styling: wire colour encodes signal
/// <em>type</em> and never health, and a node's <see cref="MapDevice.Tier"/> says how the app
/// knows its state — so "probably fine" can never be drawn the same as "checked".
/// </para>
/// </summary>
public sealed class SystemMap
{
    /// <summary>Shown as the map's title, and on any device that links to it.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Free text under the title — what this map covers and what it does not.</summary>
    public string? Summary { get; set; }

    public List<MapDevice> Devices { get; set; } = new();

    public List<MapConnection> Connections { get; set; } = new();

    /// <summary>
    /// Optional role columns drawn as faint bands behind the graph — SOURCES, BOOTH,
    /// DISTRIBUTION, OUTPUTS. Layout furniture, not topology: they carry rank so the node boxes
    /// do not have to.
    /// </summary>
    public List<MapColumn> Columns { get; set; } = new();

    /// <summary>
    /// Free-standing notes pinned to the canvas. Not devices, not connections — the things a map
    /// cannot say in boxes and lines: "this run goes through the ceiling, do not pull it",
    /// "spare cable is in the drawer under the amp", "ask Dave before touching".
    /// <para>
    /// Deliberately unverifiable and unrolled-up. A note never affects whether the system reads
    /// green, because the moment a note could change the verdict, people start writing notes to
    /// make the verdict what they want.
    /// </para>
    /// </summary>
    public List<MapNote> Notes { get; set; } = new();

    /// <summary>File this was loaded from, so a device can link to it and the editor can save it.</summary>
    [JsonIgnore]
    public string SourceFile { get; set; } = string.Empty;
}

/// <summary>A note pinned to the canvas.</summary>
public sealed class MapNote
{
    public string Id { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>
    /// Optional <see cref="MapDevice.Id"/> this note is about. An attached note travels with its
    /// device when the box moves, and draws a faint tether to it, so the note that says "the left
    /// XLR is intermittent" cannot drift away from the thing it is warning you about.
    /// </summary>
    public string? AboutDevice { get; set; }

    /// <summary>One of <see cref="MapNoteTones"/>. Presentation only — never a health signal.</summary>
    public string Tone { get; set; } = MapNoteTones.Plain;

    /// <summary>Who pinned it, if the author cared to say.</summary>
    public string? Author { get; set; }
}

/// <summary>
/// A note's visual weight. A warning note is drawn in amber, but it is still only a note: it says
/// "read me", not "something is wrong", and it never touches the rollup.
/// </summary>
public static class MapNoteTones
{
    public const string Plain = "plain";
    public const string Warning = "warning";

    public static IReadOnlyList<string> All { get; } = new[] { Plain, Warning };
}

/// <summary>A labelled vertical band behind the graph.</summary>
public sealed class MapColumn
{
    public string Label { get; set; } = string.Empty;

    public double X { get; set; }
}

/// <summary>
/// How the app knows a device's state. The load-bearing idea from the handoff's off-campus work:
/// a map that draws Facebook the same way it draws the audio console is a map that lies.
/// </summary>
public static class MapTiers
{
    /// <summary>The app checks it directly. The only tier allowed to hold the readiness gate.</summary>
    public const string Verified = "verified";

    /// <summary>A third party's API tells us. Believed, badged, and never allowed to block.</summary>
    public const string Reported = "reported";

    /// <summary>Assumed fine because its upstream is fine. Drawn hollow; never shows a green dot.</summary>
    public const string Inferred = "inferred";

    /// <summary>Somebody's job to confirm. Becomes a manual checklist item, not a machine check.</summary>
    public const string Human = "human";
}

/// <summary>A box on the map: a camera, a console, a switch, a streaming platform.</summary>
public sealed class MapDevice
{
    /// <summary>
    /// Stable identifier, referenced by <see cref="MapConnection.From"/> and <c>To</c>. The
    /// editor generates one; a hand-written map can use anything readable. Renaming the label
    /// never breaks a connection, which is why this is separate from <see cref="Label"/>.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>Icon/glyph hint — <see cref="MapDeviceKinds"/>. Chooses presentation only.</summary>
    public string Kind { get; set; } = MapDeviceKinds.Device;

    /// <summary>
    /// Hubs — the PCs and consoles everything converges on — get the brighter surface and the
    /// stronger outline. Surface, not size: every node stays the same box, per the handoff.
    /// </summary>
    public bool Hub { get; set; }

    /// <summary>
    /// One of <see cref="MapTiers"/>. Absent resolves at load: verified when there is a
    /// <see cref="Verify"/>, inferred when there is not — an unchecked device is a guess, and a
    /// guess must be drawn as one.
    /// </summary>
    public string? Tier { get; set; }

    /// <summary>The mono sub-line: transport and address. <c>NDI · 10.0.1.21</c>, <c>X32 · 32 IN</c>.</summary>
    public string? Detail { get; set; }

    /// <summary>Logical (signal-flow) position. The building plan gets its own later — the two
    /// views are different projections, not one layout.</summary>
    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>
    /// The device's dominant signal type — a <see cref="MapConnectionType.Id"/>. Drives the 3px
    /// left accent bar, dashed when that type is wireless.
    /// </summary>
    public string? DominantType { get; set; }

    /// <summary>
    /// Beyond the property line. Enforced in the model: nothing off campus may block
    /// <c>Ready to go</c>, whatever its tier claims — off-campus trouble is a techdesk banner,
    /// never a volunteer's red checklist.
    /// </summary>
    public bool OffCampus { get; set; }

    /// <summary>
    /// Optional. Polled through the same verifier registry as the checklist — the map's checks
    /// and the checklist's verifiers are deliberately the same mechanism.
    /// </summary>
    public VerifySpec? Verify { get; set; }

    /// <summary>Optional file name of another map. Clicking drills in; its health rolls up here.</summary>
    public string? LinksTo { get; set; }

    /// <summary>What to tell whoever is standing in front of it, in the author's words.</summary>
    public List<string> CheckSteps { get; set; } = new();

    /// <summary>Where the thing physically is. "Grey box on the shelf behind the booth."</summary>
    public string? Location { get; set; }

    /// <summary>
    /// Named sockets on the box — <c>AES50 A</c>, <c>CH 25-26</c>, <c>MAIN L/R</c>. Optional, and
    /// deliberately so: a map is useful the moment two boxes are joined, and demanding a port list
    /// before you can draw a line would make the first five minutes miserable.
    /// <para>
    /// What they buy you is the difference between "the console is fed from the stage box" and
    /// "the stage box arrives on AES50 A" — which is the sentence somebody needs while they are
    /// standing behind the rack with a torch. A device with no ports falls back to spreading its
    /// wires evenly along the edge, exactly as before.
    /// </para>
    /// </summary>
    public List<MapPort> Ports { get; set; } = new();
}

/// <summary>
/// One socket on a device. Ports are anchors with names: a connection that names one lands on it
/// instead of on an arbitrary point along the edge.
/// </summary>
public sealed class MapPort
{
    /// <summary>Stable id, referenced by <see cref="MapConnection.FromPort"/> and <c>ToPort</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>What is silkscreened next to it. <c>AES50 A</c>, <c>OUT 1</c>, <c>HDMI 2</c>.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>One of <see cref="MapPortSides"/>. Decides which edge of the box it sits on.</summary>
    public string Side { get; set; } = MapPortSides.In;

    /// <summary>
    /// Optional <see cref="MapConnectionType.Id"/> this socket accepts. Advisory: the editor warns
    /// when a wire lands somewhere its signal cannot physically go, but never refuses. Real
    /// buildings contain adapters, and a map that argues with the building loses.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>Optional sub-line — <c>CHANNELS 1-8</c>, <c>REAR PANEL</c>.</summary>
    public string? Detail { get; set; }
}

/// <summary>
/// Which edge a port lives on. The graph flows left to right, so inputs face left and outputs
/// face right. <see cref="Both"/> is for the sockets that genuinely carry traffic each way — an
/// AES50 jack, an ethernet port — and shows on whichever edge a given wire needs.
/// </summary>
public static class MapPortSides
{
    public const string In = "in";
    public const string Out = "out";
    public const string Both = "both";

    public static IReadOnlyList<string> All { get; } = new[] { In, Out, Both };

    /// <summary>Can a wire <em>arriving</em> at this device land here?</summary>
    public static bool AcceptsIn(string? side) => side is In or Both;

    /// <summary>Can a wire <em>leaving</em> this device start here?</summary>
    public static bool AcceptsOut(string? side) => side is Out or Both;
}

/// <summary>Glyph hints. Recognised values only; anything else draws as a plain device.</summary>
public static class MapDeviceKinds
{
    public const string Camera = "camera";
    public const string Switcher = "switcher";
    public const string Computer = "computer";
    public const string Audio = "audio";
    public const string Lighting = "lighting";
    public const string Network = "network";
    public const string Display = "display";
    public const string Cloud = "cloud";
    public const string Device = "device";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        Camera, Switcher, Computer, Audio, Lighting, Network, Display, Cloud, Device,
    };
}

/// <summary>A line between two devices, and the signal it claims to carry.</summary>
public sealed class MapConnection
{
    /// <summary>Stable id — selection, and the seed for this wire's animation speed.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary><see cref="MapDevice.Id"/> of the source.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary><see cref="MapDevice.Id"/> of the destination.</summary>
    public string To { get; set; } = string.Empty;

    /// <summary>A <see cref="MapConnectionType.Id"/>. The type owns colour, dash and speed.</summary>
    public string? Type { get; set; }

    /// <summary>
    /// This run carries signal both ways on one cable — an AES50 snake sending inputs up and IEM
    /// mixes back, a network trunk, a USB link. Drawn as one wire with flow drifting in both
    /// directions rather than two wires stacked on top of each other.
    /// <para>
    /// It is a topology claim as much as a drawing one: a bidirectional run is walked from either
    /// end when the map works out what starves when something dies, because losing the cable
    /// really does cost you both directions at once.
    /// </para>
    /// </summary>
    public bool Bidirectional { get; set; }

    /// <summary>
    /// Optional <see cref="MapPort.Id"/> on the source device this run leaves from. Null means
    /// "somewhere on that edge" and the map spreads it evenly with the device's other wires.
    /// </summary>
    public string? FromPort { get; set; }

    /// <summary>Optional <see cref="MapPort.Id"/> on the destination device this run lands on.</summary>
    public string? ToPort { get; set; }

    /// <summary>Optional free text beyond the type — port numbers, universe, channel range.</summary>
    public string? Label { get; set; }

    /// <summary>
    /// Optional, and the most valuable field on the map: a verifier here answers "does the
    /// signal actually arrive?" rather than "are both ends switched on?".
    /// </summary>
    public VerifySpec? Verify { get; set; }

    public List<string> CheckSteps { get; set; } = new();

    /// <summary>Cable length, drawn as the midpoint badge. Documentation, not measurement.</summary>
    public int? LengthFt { get; set; }

    /// <summary>
    /// A path that exists but is not carrying anything by design — the direct-to-YouTube backup.
    /// Drawn grey, dashed, and deliberately unanimated: stillness is the signal.
    /// </summary>
    public bool Standby { get; set; }

    /// <summary>
    /// Stable per-wire animation seed, assigned at creation. No two adjacent wires should share
    /// a flow speed — identical durations make the map throb in lockstep, which is the
    /// difference between "alive" and "loading spinner" — and the jitter must survive reloads.
    /// </summary>
    public int FlowSeed { get; set; }
}

/// <summary>
/// A signal type: XLR, NDI, Dante. Owns the wire's colour, dash pattern and flow speed.
/// <para>
/// This is the load-bearing rule of the whole feature — wire colour encodes signal type, never
/// health. Health lives on node dots, badges and the one reserved red dash pattern, each with a
/// non-colour cue, so a colour-blind operator can still read the map.
/// </para>
/// </summary>
public sealed class MapConnectionType
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Hex sRGB. Built-ins are the handoff's oklch palette, converted once.</summary>
    public string Colour { get; set; } = "#6e8398";

    /// <summary>solid | dashed | double.</summary>
    public string LineStyle { get; set; } = MapLineStyles.Solid;

    public double StrokeWidth { get; set; } = 2.5;

    /// <summary>Seconds for one flow cycle. Each wire jitters around this via its seed.</summary>
    public double FlowSeconds { get; set; } = 4.0;

    /// <summary>Wireless draws with no cable stroke at all — radio is not a physical object.</summary>
    public bool Wireless { get; set; }

    /// <summary>Warn when a run exceeds this. HDMI over Cat6 warns over 50 ft.</summary>
    public int? WarnOverFt { get; set; }

    /// <summary>Legend/filter grouping: video | audio | lighting | network.</summary>
    public string? Category { get; set; }

    public bool BuiltIn { get; set; }

    public string? CreatedBy { get; set; }

    public string? CreatedAt { get; set; }
}

public static class MapLineStyles
{
    public const string Solid = "solid";
    public const string Dashed = "dashed";
    public const string Double = "double";
}

/// <summary>
/// The built-in signal types, colours converted once from the handoff's oklch values. Related
/// types share a hue and differ by line style — wireless audio is XLR's gold, lighter and
/// dashed — which is what makes the legend learnable. Custom types belong in unused hues.
/// </summary>
public static class MapConnectionTypes
{
    public static IReadOnlyList<MapConnectionType> BuiltIn { get; } = new[]
    {
        new MapConnectionType { Id = "xlr", Name = "XLR analog audio", Colour = "#ffd45e", FlowSeconds = 3.8, Category = "audio", BuiltIn = true },
        new MapConnectionType { Id = "wl-audio", Name = "Wireless audio", Colour = "#ffe9a8", LineStyle = MapLineStyles.Dashed, StrokeWidth = 2, FlowSeconds = 6.6, Wireless = true, Category = "audio", BuiltIn = true },
        new MapConnectionType { Id = "dante", Name = "Dante / audio over IP", Colour = "#3ce0e8", FlowSeconds = 3.4, Category = "audio", BuiltIn = true },
        new MapConnectionType { Id = "aes50", Name = "AES50 / digital snake", Colour = "#67f0c8", FlowSeconds = 3.0, Category = "audio", BuiltIn = true },
        new MapConnectionType { Id = "analog-snake", Name = "Analog snake", Colour = "#e0a866", FlowSeconds = 4.2, Category = "audio", BuiltIn = true },
        new MapConnectionType { Id = "ndi", Name = "NDI", Colour = "#6bb6ff", FlowSeconds = 3.2, Category = "video", BuiltIn = true },
        new MapConnectionType { Id = "sdi", Name = "SDI", Colour = "#ef9bf5", FlowSeconds = 3.6, Category = "video", BuiltIn = true },
        new MapConnectionType { Id = "hdmi", Name = "HDMI", Colour = "#ffa585", FlowSeconds = 4.0, WarnOverFt = 50, Category = "video", BuiltIn = true },
        new MapConnectionType { Id = "dmx", Name = "DMX 512", Colour = "#b795ff", FlowSeconds = 4.4, Category = "lighting", BuiltIn = true },
        new MapConnectionType { Id = "wl-dmx", Name = "Wireless DMX", Colour = "#d4c2ff", LineStyle = MapLineStyles.Dashed, StrokeWidth = 2, FlowSeconds = 6.8, Wireless = true, Category = "lighting", BuiltIn = true },
        new MapConnectionType { Id = "wl-video", Name = "Wireless video", Colour = "#f7b6fb", LineStyle = MapLineStyles.Dashed, StrokeWidth = 2, FlowSeconds = 6.4, Wireless = true, Category = "video", BuiltIn = true },
        new MapConnectionType { Id = "cat6", Name = "Network · Cat6", Colour = "#8ba3bd", FlowSeconds = 5.0, Category = "network", BuiltIn = true },
    };

    /// <summary>The fallback for a connection whose type is missing or unknown.</summary>
    public static MapConnectionType Unknown { get; } = new()
    {
        Id = "unknown",
        Name = "Unknown type",
        Colour = "#8ba3bd",
        LineStyle = MapLineStyles.Dashed,
        StrokeWidth = 2,
        FlowSeconds = 5.0,
        BuiltIn = true,
    };
}
