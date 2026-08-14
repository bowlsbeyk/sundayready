namespace SundayReady.Models;

/// <summary>
/// Local <c>station.json</c>: which station this PC is and what it loads. Absent means
/// fall back to hostname auto-detect.
/// </summary>
public sealed class StationConfig
{
    public string Station { get; set; } = string.Empty;

    public List<string> Checklists { get; set; } = new();

    /// <summary>Shown in the rail footer, and used as the default sign-off attribution.</summary>
    public string? Operator { get; set; }

    public ServiceTimes? Service { get; set; }

    public List<QuickLaunchTile> QuickLaunch { get; set; } = new();

    /// <summary>Selects the techdesk aggregation view instead of this PC's own checklist.</summary>
    public bool Techdesk { get; set; }

    public UpdateSettings Updates { get; set; } = new();

    public ViewerCountSettings ViewerCounts { get; set; } = new();
}

/// <summary>
/// Live audience figures for the techdesk. Telemetry, never an input to readiness.
/// </summary>
public sealed class ViewerCountSettings
{
    public bool Enabled { get; set; }

    /// <summary>A YouTube Data API v3 key. Reading a count costs one quota unit of 10,000/day.</summary>
    public string? YouTubeApiKey { get; set; }

    /// <summary>
    /// The church's channel. The app finds whatever is live on it — which costs 100 quota
    /// units, so it is resolved once per session rather than on every poll.
    /// </summary>
    public string? YouTubeChannelId { get; set; }

    /// <summary>
    /// Optional. Pin a specific broadcast (id or URL) to skip the channel search entirely.
    /// </summary>
    public string? YouTubeVideoId { get; set; }
}

public sealed class UpdateSettings
{
    /// <summary><c>owner/repo</c>. Defaults to the shipped repository when unset.</summary>
    public string? Repository { get; set; }

    /// <summary>Checks on startup and stages what it finds; the swap happens at next launch.</summary>
    public bool Enabled { get; set; } = true;
}

public sealed class ServiceTimes
{
    /// <summary>24-hour <c>HH:mm</c>. Drives the countdown.</summary>
    public string? StartsAt { get; set; }

    public string? StreamAt { get; set; }

    public string? DoorsAt { get; set; }

    /// <summary>Free text under the countdown, e.g. <c>SANCTUARY</c>.</summary>
    public string? Venue { get; set; }
}

public sealed class QuickLaunchTile
{
    public string Label { get; set; } = string.Empty;

    public ActionSpec? Action { get; set; }
}
