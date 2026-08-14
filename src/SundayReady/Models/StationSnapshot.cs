namespace SundayReady.Models;

/// <summary>How one item stood at the moment the snapshot was taken.</summary>
public static class SnapshotItemStates
{
    public const string Done = "done";
    public const string Open = "open";
    public const string Polling = "polling";
    public const string Failing = "failing";
}

/// <summary>
/// One checklist item, flattened for the techdesk. Deliberately not the full
/// <see cref="ChecklistItem"/>: the techdesk shows what a station is doing, not how it is
/// configured, and the share is read by a build that may be older than the one writing it.
/// </summary>
public sealed class SnapshotItem
{
    public string Label { get; set; } = string.Empty;

    public string Tab { get; set; } = string.Empty;

    /// <summary>One of <see cref="SnapshotItemStates"/>.</summary>
    public string State { get; set; } = SnapshotItemStates.Open;

    /// <summary>manual / action / verified, so the techdesk can say "MANUAL ITEM · UNCHECKED".</summary>
    public string Type { get; set; } = ChecklistItemTypes.Manual;

    /// <summary>The verifier's own words about its last attempt, when it has any.</summary>
    public string? Detail { get; set; }

    /// <summary>When this item first went red, so the board can say how long it has been failing.</summary>
    public DateTimeOffset? FailingSince { get; set; }

    public DateTimeOffset? LastPassAt { get; set; }
}

/// <summary>
/// What one station publishes to the techdesk share every 15 seconds.
/// <para>
/// A file, not a socket: booth PCs already have a share mapped, nothing needs a listening
/// port opened on a church network, and a station that is switched off simply stops
/// touching its file — which is exactly the "no heartbeat" signal the techdesk wants.
/// </para>
/// </summary>
public sealed class StationSnapshot
{
    public string Station { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    /// <summary>This PC's IPv4 address. The techdesk device list shows its last octet.</summary>
    public string? Address { get; set; }

    public int Percentage { get; set; }

    public int Completed { get; set; }

    public int Total { get; set; }

    public int Failing { get; set; }

    public string? Operator { get; set; }

    /// <summary>Set once every item on every tab is accounted for. Null while the gate is shut.</summary>
    public DateTimeOffset? ReadyAt { get; set; }

    public DateTimeOffset LastHeartbeat { get; set; }

    /// <summary>
    /// Round trip reported by this station's <c>internetReachable</c> verifier, when it has
    /// one. The only network number in the app that is actually measured.
    /// </summary>
    public int? PingMs { get; set; }

    public List<SnapshotItem> Items { get; set; } = new();
}
