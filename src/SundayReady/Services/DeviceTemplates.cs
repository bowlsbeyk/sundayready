using SundayReady.Models;

namespace SundayReady.Services;

/// <summary>One socket a template defines. Turned into a real <see cref="MapPort"/> on apply.</summary>
public sealed record DeviceTemplatePort(string Label, string Side, string? Detail = null, string? Type = null);

/// <summary>
/// A piece of gear the app already knows: its sockets, its kind, its dominant signal.
/// <para>
/// The handoff's premise is that nobody should ever type a 48-row port list: ports come from the
/// template, then get edited. A template is a starting point, not a lock — every port it adds is
/// an ordinary editable row the moment it lands.
/// </para>
/// </summary>
public sealed class DeviceTemplate
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = MapDeviceKinds.Device;

    public string? DominantType { get; set; }

    public bool Hub { get; set; }

    public List<DeviceTemplatePort> Ports { get; set; } = new();
}

/// <summary>
/// What a template share file carries: one or more templates, and who made them.
/// <para>
/// This is the community format. Somebody with a Yamaha desk types its port list once, saves it
/// as a template, exports this file, and posts it — and every church with the same desk imports
/// it instead of typing. Import never overwrites: a template whose name is already known is
/// skipped, because "your import replaced my template" is how sharing stops.
/// </para>
/// </summary>
public sealed class DeviceTemplateExport
{
    public string Format { get; set; } = "sundayready-templates";

    public int Version { get; set; } = 1;

    /// <summary>Free text about who made it — a name, a church, a forum handle.</summary>
    public string? ExportedBy { get; set; }

    public List<DeviceTemplate> Templates { get; set; } = new();
}

/// <summary>Reads template JSON in any of the three shapes people actually write.</summary>
public static class DeviceTemplateReader
{
    /// <summary>
    /// Bundle, bare array, or a single template — each attempt insulated, because an
    /// array-shaped file makes the bundle parse throw and that must not take the fallbacks
    /// down with it. Returns an empty list rather than throwing: a community folder with one
    /// bad file in it should lose that file, not the folder.
    /// </summary>
    public static List<DeviceTemplate> Parse(string json)
    {
        static T? Try<T>(string text) where T : class
        {
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<T>(text, ChecklistLoader.JsonOptions);
            }
            catch (Exception)
            {
                return null;
            }
        }

        var bundle = Try<DeviceTemplateExport>(json);

        var list = bundle is { Templates.Count: > 0 }
            ? bundle.Templates
            : Try<List<DeviceTemplate>>(json)
                ?? (Try<DeviceTemplate>(json) is { Ports.Count: > 0 } one
                    ? new List<DeviceTemplate> { one }
                    : new List<DeviceTemplate>());

        return list
            .Where(t => !string.IsNullOrWhiteSpace(t.Name) && t.Ports.Count > 0)
            .ToList();
    }
}

/// <summary>
/// The built-in catalogue. Curated for the gear that actually turns up in church A/V closets, and
/// deliberately small: a template that is wrong for your unit is worse than typing, so these stay
/// close to the common configurations and let the editor handle the variants.
/// </summary>
public static class DeviceTemplates
{
    /// <summary>A numbered run of identical sockets: <c>CH 1 … CH 32</c>.</summary>
    private static IEnumerable<DeviceTemplatePort> Range(
        string prefix, int from, int to, string side, string? type = null)
    {
        for (var i = from; i <= to; i++)
        {
            yield return new DeviceTemplatePort($"{prefix} {i}", side, Type: type);
        }
    }

    private static List<DeviceTemplatePort> Build(params IEnumerable<DeviceTemplatePort>[] groups) =>
        groups.SelectMany(g => g).ToList();

    public static IReadOnlyList<DeviceTemplate> BuiltIn { get; } = new[]
    {
        new DeviceTemplate
        {
            Id = "x32",
            Name = "Behringer X32",
            Kind = MapDeviceKinds.Audio,
            DominantType = "xlr",
            Hub = true,
            Ports = Build(
                Range("CH", 1, 32, MapPortSides.In, "xlr"),
                Range("OUT", 1, 16, MapPortSides.Out, "xlr"),
                new[]
                {
                    new DeviceTemplatePort("AES50 A", MapPortSides.Both, Type: "aes50"),
                    new DeviceTemplatePort("AES50 B", MapPortSides.Both, Type: "aes50"),
                    new DeviceTemplatePort("ETHERNET", MapPortSides.Both, Type: "cat6"),
                }),
        },
        new DeviceTemplate
        {
            Id = "x32-compact",
            Name = "Behringer X32 Compact",
            Kind = MapDeviceKinds.Audio,
            DominantType = "xlr",
            Hub = true,
            Ports = Build(
                Range("CH", 1, 16, MapPortSides.In, "xlr"),
                Range("OUT", 1, 8, MapPortSides.Out, "xlr"),
                new[]
                {
                    new DeviceTemplatePort("AES50 A", MapPortSides.Both, Type: "aes50"),
                    new DeviceTemplatePort("AES50 B", MapPortSides.Both, Type: "aes50"),
                    new DeviceTemplatePort("ETHERNET", MapPortSides.Both, Type: "cat6"),
                }),
        },
        new DeviceTemplate
        {
            Id = "s16",
            Name = "Behringer S16 stage box",
            Kind = MapDeviceKinds.Audio,
            DominantType = "aes50",
            Ports = Build(
                Range("INPUT", 1, 16, MapPortSides.In, "xlr"),
                Range("OUT", 1, 8, MapPortSides.Out, "xlr"),
                new[]
                {
                    new DeviceTemplatePort("AES50 A", MapPortSides.Both, Type: "aes50"),
                    new DeviceTemplatePort("AES50 B", MapPortSides.Both, Type: "aes50"),
                }),
        },
        new DeviceTemplate
        {
            Id = "s32",
            Name = "Behringer S32 stage box",
            Kind = MapDeviceKinds.Audio,
            DominantType = "aes50",
            Ports = Build(
                Range("INPUT", 1, 32, MapPortSides.In, "xlr"),
                Range("OUT", 1, 16, MapPortSides.Out, "xlr"),
                new[]
                {
                    new DeviceTemplatePort("AES50 A", MapPortSides.Both, Type: "aes50"),
                    new DeviceTemplatePort("AES50 B", MapPortSides.Both, Type: "aes50"),
                }),
        },
        new DeviceTemplate
        {
            Id = "switch-24",
            Name = "Network switch · 24 port",
            Kind = MapDeviceKinds.Network,
            DominantType = "cat6",
            Hub = true,
            Ports = Build(Range("PORT", 1, 24, MapPortSides.Both, "cat6")),
        },
        new DeviceTemplate
        {
            Id = "switch-8",
            Name = "Network switch · 8 port",
            Kind = MapDeviceKinds.Network,
            DominantType = "cat6",
            Ports = Build(Range("PORT", 1, 8, MapPortSides.Both, "cat6")),
        },
        new DeviceTemplate
        {
            Id = "rx4",
            Name = "Wireless receiver rack · 4 channel",
            Kind = MapDeviceKinds.Audio,
            DominantType = "wl-audio",
            Ports = Build(
                Range("RF", 1, 4, MapPortSides.In, "wl-audio"),
                Range("OUT", 1, 4, MapPortSides.Out, "xlr")),
        },
        new DeviceTemplate
        {
            Id = "scarlett-18i20",
            Name = "Focusrite Scarlett 18i20",
            Kind = MapDeviceKinds.Audio,
            DominantType = "xlr",
            Ports = Build(
                Range("IN", 1, 8, MapPortSides.In, "xlr"),
                Range("LINE OUT", 1, 10, MapPortSides.Out, "xlr"),
                new[] { new DeviceTemplatePort("USB", MapPortSides.Both) }),
        },
        new DeviceTemplate
        {
            Id = "playback-pc",
            Name = "Presentation PC (ProPresenter / slides)",
            Kind = MapDeviceKinds.Computer,
            DominantType = "cat6",
            Ports = new List<DeviceTemplatePort>
            {
                new DeviceTemplatePort("NDI OUT", MapPortSides.Out, Type: "ndi"),
                new DeviceTemplatePort("HDMI OUT", MapPortSides.Out, Type: "hdmi"),
                new DeviceTemplatePort("AUDIO OUT", MapPortSides.Out, "USB INTERFACE", "xlr"),
                new DeviceTemplatePort("ETHERNET", MapPortSides.Both, Type: "cat6"),
            },
        },
    };
}
