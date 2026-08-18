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

    /// <summary>File this was loaded from, so a device can link to it and the editor can save it.</summary>
    [JsonIgnore]
    public string SourceFile { get; set; } = string.Empty;
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
