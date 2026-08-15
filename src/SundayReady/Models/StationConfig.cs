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

    /// <summary>
    /// When the checklist starts again. One of <see cref="ResetModes"/>.
    /// <para>
    /// Null means "not chosen", which falls back to <see cref="ResetOnRestart"/> so an older
    /// station.json keeps behaving the way it was set up.
    /// </para>
    /// </summary>
    public string? ResetMode { get; set; }

    /// <summary>The original switch. Superseded by <see cref="ResetMode"/>, still honoured.</summary>
    public bool ResetOnRestart { get; set; } = true;

    /// <summary>What <see cref="ResetMode"/> actually resolves to. Derived, so never written.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string EffectiveResetMode => ResetMode switch
    {
        ResetModes.EveryLaunch or ResetModes.PowerCycle or ResetModes.Daily => ResetMode,
        _ => ResetOnRestart ? ResetModes.EveryLaunch : ResetModes.Daily,
    };

    /// <summary>
    /// Folder every station writes its heartbeat snapshot to and the techdesk reads. A UNC
    /// path in the building; unset falls back to a local folder so techdesk mode can be
    /// driven on one PC before anyone has picked a share.
    /// </summary>
    public string? TechdeskShare { get; set; }

    /// <summary>
    /// Which techdesk layout this screen shows — see <see cref="TechdeskLayouts"/>. Both are
    /// built: the design offered them as alternatives and the choice belongs to whoever is
    /// standing in front of the actual display.
    /// </summary>
    public string TechdeskLayout { get; set; } = TechdeskLayouts.Columns;

    /// <summary>Silence longer than this renders a station as not staffed.</summary>
    public int TechdeskHeartbeatMinutes { get; set; } = 22;

    /// <summary>
    /// What "Page the volunteer" runs — a <c>tel:</c> or <c>sms:</c> link, a chat webhook
    /// script, whatever the church actually uses. <c>{station}</c> and <c>{operator}</c> in
    /// the arguments are substituted. Without one the button says so rather than pretending.
    /// </summary>
    public ActionSpec? TechdeskPage { get; set; }

    public UpdateSettings Updates { get; set; } = new();

    public ViewerCountSettings ViewerCounts { get; set; } = new();
}

/// <summary>When the checklist starts again.</summary>
public static class ResetModes
{
    /// <summary>
    /// Clear whenever SundayReady starts. Blunt, but it never fails to notice — unlike
    /// anything that tries to work out whether the machine was really off, which sleep, Fast
    /// Startup and hibernation all defeat in different ways.
    /// </summary>
    public const string EveryLaunch = "everyLaunch";

    /// <summary>Clear only when the PC has actually been off and on.</summary>
    public const string PowerCycle = "powerCycle";

    /// <summary>Clear only on a new calendar day.</summary>
    public const string Daily = "daily";
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

    /// <summary>
    /// The church's Facebook Page id. The Page access token that goes with it is NOT stored
    /// here — see <see cref="Services.SecretStore"/>. A token in a file people copy between
    /// stations is a token that leaks.
    /// </summary>
    public string? FacebookPageId { get; set; }
}

public static class TechdeskLayouts
{
    /// <summary>1c — a column per station plus a telemetry rail. For a desk that is worked in.</summary>
    public const string Columns = "columns";

    /// <summary>1d — big type, exceptions only. For a screen glanced at from across the room.</summary>
    public const string Board = "board";
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
    /// <summary>
    /// Every service on a service day, 24-hour <c>HH:mm</c>. The countdown targets the next
    /// one, and the checklist starts again at each changeover — which is how a PC that is
    /// never switched off gets a fresh list for the second service.
    /// </summary>
    public List<string> Starts { get; set; } = new();

    /// <summary>
    /// How long before a service the station starts preparing for it. That moment is also
    /// when the previous service's checklist is cleared.
    /// </summary>
    public int ResetLeadMinutes { get; set; } = Services.ServiceSchedule.DefaultLeadMinutes;

    /// <summary>
    /// The original single service time. Still honoured so existing station.json files work,
    /// but <see cref="Starts"/> is the model now.
    /// </summary>
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
