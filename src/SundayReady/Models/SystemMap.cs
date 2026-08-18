using System.Text.Json.Serialization;

namespace SundayReady.Models;

/// <summary>
/// One picture of how the building is wired: boxes and the links between them.
/// <para>
/// A map is a diagnostic layer, not a procedural one. A checklist asks "has somebody done the
/// things?"; a map asks "is the signal path intact right now?" — and it can answer the question a
/// checklist structurally cannot, which is <em>where</em> a path broke. A camera that is powered
/// and a switcher that is running still tell you nothing about whether the camera reaches the
/// switcher. That failure lives on the connection, so this model lets a connection be verified in
/// its own right.
/// </para>
/// </summary>
public sealed class SystemMap
{
    /// <summary>Shown as the map's title, and on the component that links to it.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Free text under the title — what this map covers and what it does not.</summary>
    public string? Summary { get; set; }

    public List<MapComponent> Components { get; set; } = new();

    public List<MapConnection> Connections { get; set; } = new();

    /// <summary>File this was loaded from, so a component can link to it and the editor can save it.</summary>
    [JsonIgnore]
    public string SourceFile { get; set; } = string.Empty;
}

/// <summary>
/// A box on the map: a camera, a switcher, a network drop, a whole other map.
/// </summary>
public sealed class MapComponent
{
    /// <summary>
    /// Stable identifier, referenced by <see cref="MapConnection.From"/> and <c>To</c>. The editor
    /// generates one; a hand-written map can use anything readable. Renaming the label never
    /// breaks a connection, which is the whole reason this is separate from <see cref="Label"/>.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// What sort of thing this is — <see cref="MapComponentKinds"/>. Chooses the icon and nothing
    /// else; a map with every box the same shape is hard to read at a glance and that is the only
    /// job this field has.
    /// </summary>
    public string Kind { get; set; } = MapComponentKinds.Device;

    /// <summary>Canvas position. The editor writes these; nothing else depends on them.</summary>
    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>
    /// Optional. When present the component is polled exactly like a checklist item, using the
    /// same verifier registry — there is no separate notion of a "map check".
    /// </summary>
    public VerifySpec? Verify { get; set; }

    /// <summary>
    /// Optional file name of another map. Clicking the component opens it, and that map's health
    /// rolls up into this box — so the top-level map goes red because something three levels down
    /// did, which is the point of having levels at all.
    /// </summary>
    public string? LinksTo { get; set; }

    /// <summary>
    /// What to tell whoever is standing in front of it. Same intent as a checklist item's
    /// checkSteps: the app cannot write this and it is the most useful thing on the box.
    /// </summary>
    public List<string> CheckSteps { get; set; } = new();

    /// <summary>Where the thing physically is. "Grey box on the shelf behind the booth."</summary>
    public string? Location { get; set; }
}

/// <summary>A line between two components, optionally verified in its own right.</summary>
public sealed class MapConnection
{
    /// <summary><see cref="MapComponent.Id"/> of the source.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary><see cref="MapComponent.Id"/> of the destination.</summary>
    public string To { get; set; } = string.Empty;

    /// <summary>Drawn along the line — "NDI", "SDI 3G", "XLR 1-2", "RTMP".</summary>
    public string? Label { get; set; }

    /// <summary>
    /// Optional, and the most valuable field on the map. A verifier here answers "does the signal
    /// actually arrive?" rather than "are both ends switched on?".
    /// </summary>
    public VerifySpec? Verify { get; set; }

    public List<string> CheckSteps { get; set; } = new();
}

/// <summary>Icon shapes. Recognised values only; anything else draws as a plain device.</summary>
public static class MapComponentKinds
{
    public const string Camera = "camera";
    public const string Switcher = "switcher";
    public const string Computer = "computer";
    public const string Audio = "audio";
    public const string Network = "network";
    public const string Display = "display";
    public const string Cloud = "cloud";
    public const string Device = "device";

    /// <summary>Offered in the editor, in the order they appear in the picker.</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        Camera, Switcher, Computer, Audio, Network, Display, Cloud, Device,
    };
}
