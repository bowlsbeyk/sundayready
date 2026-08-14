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
