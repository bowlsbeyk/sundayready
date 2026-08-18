using SundayReady.Models;
using SundayReady.Services;

namespace SundayReady.ViewModels;

/// <summary>
/// Which projection of the same map is on screen. The handoff is firm that these are three views
/// of one dataset rather than three drawings: the signal flow answers "what feeds what", the
/// building answers "which room do I walk to", and the stream path answers "how far did it get
/// before it died". Nothing here re-enters data — a device's room and its wires already say it.
/// </summary>
public enum MapViewMode
{
    SignalFlow,
    Building,
    StreamPath,
}

/// <summary>
/// One room, and everything in it.
/// <para>
/// The point of the building view is the walk. When something fails at 09:52 the question stops
/// being "which device" and becomes "where do I go, and what do I carry" — and a signal-flow
/// diagram, which deliberately arranges boxes by what feeds what, is the worst possible answer to
/// that question. Grouping the same boxes by <see cref="MapDevice.Location"/> costs nothing and
/// answers it directly.
/// </para>
/// </summary>
public sealed class MapRoomViewModel
{
    public MapRoomViewModel(string name, IReadOnlyList<MapDeviceViewModel> devices)
    {
        Name = name;
        Devices = devices;
    }

    public string Name { get; }

    public IReadOnlyList<MapDeviceViewModel> Devices { get; }

    public int Count => Devices.Count;

    public string CountLabel => Devices.Count == 1 ? "1 THING" : $"{Devices.Count} THINGS";

    /// <summary>
    /// A room is in trouble when something in it is actually broken. Starved devices do not count:
    /// the whole point is to send somebody to the room where the fault is, not to every room
    /// downstream of it.
    /// </summary>
    public bool HasFailure => Devices.Any(d => d.ShowsFailure);

    public bool IsQuiet => !HasFailure && !Devices.Any(d => d.IsStarved);

    /// <summary>Rooms with a fault sort first — the list is a work queue, not an inventory.</summary>
    public int SortKey => HasFailure ? 0 : IsQuiet ? 2 : 1;

    public string StateLabel => HasFailure
        ? $"{Devices.Count(d => d.ShowsFailure)} NOT PASSING"
        : IsQuiet ? string.Empty : "WAITING ON SOMETHING UPSTREAM";
}

/// <summary>
/// One hop along the stream path: a device, and the run that got signal into it.
/// </summary>
public sealed class MapStreamHopViewModel
{
    public MapStreamHopViewModel(
        MapDeviceViewModel device,
        MapConnectionViewModel? arriving,
        bool isFirstBreak)
    {
        Device = device;
        Arriving = arriving;
        IsFirstBreak = isFirstBreak;
    }

    public MapDeviceViewModel Device { get; }

    /// <summary>The run feeding this hop. Null on the first hop, which is the source.</summary>
    public MapConnectionViewModel? Arriving { get; }

    public bool HasArriving => Arriving is not null;

    public string? ArrivingLabel => Arriving?.Type.Name;

    /// <summary>
    /// The one hop worth reading. Everything past a break is starved rather than broken, and
    /// marking five hops red hides the single hop that actually needs a human.
    /// </summary>
    public bool IsFirstBreak { get; }

    public bool IsStarved => !IsFirstBreak
        && (Device.IsStarved || Arriving?.FlowState == "starved");

    public bool IsOk => !IsFirstBreak && !IsStarved && Device.ShowsOk;

    public bool IsOffCampus => Device.OffCampus;
}

/// <summary>
/// Builds the two derived projections. Kept as plain functions over an already-loaded map so both
/// views stay honest by construction: they cannot show a device the signal-flow view does not have,
/// and they cannot invent a status it did not already compute.
/// </summary>
public static class MapProjections
{
    /// <summary>Devices grouped by the room they live in, faults first.</summary>
    public static IReadOnlyList<MapRoomViewModel> Rooms(SystemMapViewModel map)
    {
        const string unplaced = "No room recorded";

        return map.Devices
            .GroupBy(d => string.IsNullOrWhiteSpace(d.Location) ? unplaced : d.Location!.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(g => new MapRoomViewModel(g.Key, g.ToList()))
            .OrderBy(r => r.SortKey)
            // Unplaced devices sink to the bottom: it is a prompt to fill something in, not a room.
            .ThenBy(r => r.Name == unplaced ? 1 : 0)
            .ThenByDescending(r => r.Count)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The chain signal takes on its way out of the building.
    /// <para>
    /// "The stream path" is not a field anybody fills in, so it is worked out: the longest simple
    /// path ending at something off campus, and failing that, ending at whatever the signal finally
    /// arrives at and never leaves. That is the encoder, the streaming platform, the thing at the
    /// end — and the path to it is exactly what somebody traces with a finger when the stream drops.
    /// </para>
    /// </summary>
    public static IReadOnlyList<MapStreamHopViewModel> StreamPath(SystemMapViewModel map)
    {
        if (map.Devices.Count == 0)
        {
            return Array.Empty<MapStreamHopViewModel>();
        }

        var outgoing = map.Devices.ToDictionary(
            d => d,
            d => map.Connections.Where(c => ReferenceEquals(c.From, d)).ToList());

        // Prefer somewhere off campus; otherwise a genuine terminus.
        var targets = map.Devices.Where(d => d.OffCampus).ToList();

        if (targets.Count == 0)
        {
            targets = map.Devices.Where(d => outgoing[d].Count == 0
                && map.Connections.Any(c => ReferenceEquals(c.To, d))).ToList();
        }

        if (targets.Count == 0)
        {
            return Array.Empty<MapStreamHopViewModel>();
        }

        List<MapConnectionViewModel>? best = null;

        foreach (var target in targets)
        {
            var path = LongestPathTo(map, target);

            if (path is not null && (best is null || path.Count > best.Count))
            {
                best = path;
            }
        }

        if (best is null || best.Count == 0)
        {
            return Array.Empty<MapStreamHopViewModel>();
        }

        var hops = new List<MapStreamHopViewModel>();
        var broken = false;

        // The first hop is the source: nothing arrives into it.
        var source = best[0].From;
        var sourceBroken = source.ShowsFailure;
        broken = sourceBroken;
        hops.Add(new MapStreamHopViewModel(source, null, sourceBroken));

        foreach (var wire in best)
        {
            // The first thing that is genuinely down owns the alarm. Everything after it is
            // starved, and says so.
            var isBreak = !broken && (wire.IsDown || wire.To.ShowsFailure);
            broken |= isBreak;
            hops.Add(new MapStreamHopViewModel(wire.To, wire, isBreak));
        }

        return hops;
    }

    /// <summary>
    /// The longest simple path arriving at one device, walked backwards. Depth-first with the
    /// current chain as the visited set, so a map that loops — and a church map with a bidirectional
    /// network trunk always loops — terminates instead of chasing its own tail.
    /// </summary>
    private static List<MapConnectionViewModel>? LongestPathTo(
        SystemMapViewModel map,
        MapDeviceViewModel target)
    {
        var incoming = new Dictionary<MapDeviceViewModel, List<MapConnectionViewModel>>();

        foreach (var connection in map.Connections)
        {
            if (!incoming.TryGetValue(connection.To, out var list))
            {
                incoming[connection.To] = list = new List<MapConnectionViewModel>();
            }

            list.Add(connection);
        }

        var visiting = new HashSet<MapDeviceViewModel>();
        var guard = 0;

        List<MapConnectionViewModel>? Walk(MapDeviceViewModel node)
        {
            // A pathological map should degrade to a shorter path, never to a hung window.
            if (++guard > 4000 || !visiting.Add(node))
            {
                return new List<MapConnectionViewModel>();
            }

            List<MapConnectionViewModel>? longest = null;

            foreach (var wire in incoming.TryGetValue(node, out var list)
                         ? list
                         : Enumerable.Empty<MapConnectionViewModel>())
            {
                if (visiting.Contains(wire.From))
                {
                    continue;
                }

                var upstream = Walk(wire.From);

                if (upstream is null)
                {
                    continue;
                }

                var candidate = new List<MapConnectionViewModel>(upstream) { wire };

                if (longest is null || candidate.Count > longest.Count)
                {
                    longest = candidate;
                }
            }

            visiting.Remove(node);
            return longest ?? new List<MapConnectionViewModel>();
        }

        var path = Walk(target);
        return path is { Count: > 0 } ? path : null;
    }
}
