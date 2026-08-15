using System.Diagnostics;
using System.Text.Json;

namespace SundayReady.Services;

/// <summary>Where the station is in its Sunday.</summary>
public static class StationPhases
{
    /// <summary>Working through the checklist. The default.</summary>
    public const string Setup = "setup";

    /// <summary>Signed off. The checklist recedes and the screen watches instead.</summary>
    public const string Service = "service";

    /// <summary>The operator has said the service is over; the post-show list is the work.</summary>
    public const string PostService = "postService";
}

/// <summary>How an item came to be checked.</summary>
public static class CompletionSources
{
    public const string Manual = "manual";
    public const string Auto = "auto";
    public const string Override = "override";
}

public sealed class ItemState
{
    public bool Checked { get; set; }

    /// <summary>Operator initials, or null when the app ticked it itself.</summary>
    public string? CheckedBy { get; set; }

    public DateTimeOffset? CheckedAt { get; set; }

    public string Source { get; set; } = CompletionSources.Manual;

    /// <summary>Required when <see cref="Source"/> is override. An override without a note is not offered.</summary>
    public string? OverrideNote { get; set; }
}

/// <summary>
/// Everything about the service being set up. The checklist is per-service, so this is thrown
/// away wholesale when the PC restarts or the calendar day turns.
/// </summary>
public sealed class DailyState
{
    public DateOnly Date { get; set; }

    /// <summary>
    /// When Windows last booted, as of the moment this state was written. A different value
    /// means the PC has restarted since, which means a new service.
    /// </summary>
    public DateTimeOffset? BootedAt { get; set; }

    /// <summary>
    /// Which service occurrence these ticks belong to, from <see cref="ServiceSchedule"/>.
    /// When the station rolls over to preparing for the next one, this stops matching and the
    /// checklist starts again — the only reset a PC that is never switched off will ever see.
    /// </summary>
    public string? ServiceKey { get; set; }

    /// <summary>
    /// When the Windows session these ticks were made in began. Changes on every power-on
    /// even when Fast Startup leaves <see cref="BootedAt"/> untouched.
    /// </summary>
    public DateTimeOffset? SessionStartedAt { get; set; }

    /// <summary>Keyed by <see cref="DailyStateStore.KeyFor"/>.</summary>
    public Dictionary<string, ItemState> Items { get; set; } = new();

    public string? OperatorInitials { get; set; }

    public DateTimeOffset? SignedOffAt { get; set; }

    /// <summary>
    /// Which part of the day the station is in — see <see cref="StationPhases"/>. Persisted so
    /// an app restart mid-service does not drop the operator back into setup.
    /// </summary>
    public string? Phase { get; set; }

    /// <summary>True when the service was signed off with overridden or open items.</summary>
    public bool Partial { get; set; }
}

/// <summary>
/// Loads and saves <see cref="DailyState"/>, discarding it when the service it belongs to is
/// over: a new calendar day, or a restart of the PC.
/// <para>
/// Booth PCs are switched on for a service and off afterwards, so a restart is the truest
/// signal that this is a new one. It also handles the case a date check cannot: two services
/// on the same Sunday, where the second must not start with the first one's ticks.
/// </para>
/// </summary>
public sealed class DailyStateStore
{
    /// <summary>
    /// Boot time is derived from the uptime counter against the wall clock, and the two drift
    /// by seconds. Anything inside this window is the same boot.
    /// </summary>
    private static readonly TimeSpan BootTolerance = TimeSpan.FromMinutes(2);

    private readonly string _path;
    private readonly string _resetMode;

    public DailyStateStore(string? path = null, string? resetMode = null)
    {
        _path = path ?? AppPaths.StateFile;
        _resetMode = resetMode ?? Models.ResetModes.EveryLaunch;
    }

    /// <summary>
    /// When Windows last booted. Environment.TickCount64 is milliseconds of uptime, so the
    /// wall clock minus uptime is the moment of boot — stable across app restarts, unlike a
    /// process start time, which is exactly the distinction that matters here.
    /// </summary>
    public static DateTimeOffset BootTime() =>
        DateTimeOffset.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>
    /// When this Windows session began, approximated by the start of its Explorer.
    /// <para>
    /// Boot time alone is not enough. Windows has Fast Startup on by default, and with it a
    /// <em>Shut down</em> followed by powering the PC on is a hybrid resume: the kernel
    /// session is restored, so the boot time and the uptime counter do not change. That is
    /// exactly what a booth PC does — it gets switched off after a service and on again the
    /// next Sunday — and it would look like the machine had never been off.
    /// </para>
    /// <para>
    /// The logon does happen on every power-on, Fast Startup or not, and Explorer starts with
    /// it. So this catches the case boot time misses.
    /// </para>
    /// </summary>
    public static DateTimeOffset? SessionStartTime()
    {
        try
        {
            var session = Process.GetCurrentProcess().SessionId;

            var earliest = Process.GetProcessesByName("explorer")
                .Where(p => p.SessionId == session)
                .Select(p =>
                {
                    try
                    {
                        return (DateTime?)p.StartTime;
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                    finally
                    {
                        p.Dispose();
                    }
                })
                .Where(t => t is not null)
                .DefaultIfEmpty(null)
                .Min();

            return earliest is { } start ? new DateTimeOffset(start) : null;
        }
        catch (Exception)
        {
            // No Explorer, or no permission to ask. Boot time still applies on its own.
            return null;
        }
    }

    /// <summary>
    /// Identifies an item across runs. Scoped by source file so two tabs can carry the same
    /// label. Editing a label in the JSON drops that item's tick for the rest of the day — an
    /// acceptable trade for not needing hand-maintained ids in the checklist files.
    /// </summary>
    public static string KeyFor(string sourceFile, string label) => $"{sourceFile}|{label}";

    /// <param name="serviceKey">
    /// The service now being prepared for. State belonging to a different one is discarded.
    /// Null when no service times are configured, which disables that check.
    /// </param>
    public DailyState Load(DateOnly today, string? serviceKey = null)
    {
        var booted = BootTime();
        var session = SessionStartTime();

        // The blunt mode: the app is starting, so the checklist starts too. Nothing to detect
        // and nothing to get wrong.
        if (_resetMode == Models.ResetModes.EveryLaunch)
        {
            return new DailyState
            {
                Date = today,
                BootedAt = booted,
                SessionStartedAt = session,
                ServiceKey = serviceKey,
            };
        }

        try
        {
            if (File.Exists(_path))
            {
                var state = JsonSerializer.Deserialize<DailyState>(File.ReadAllText(_path), ChecklistLoader.JsonOptions);

                if (state is not null
                    && state.Date == today
                    && !HasRestarted(state, booted, session)
                    && !IsDifferentService(state, serviceKey))
                {
                    return state;
                }
            }
        }
        catch (Exception)
        {
            // A corrupt state file is not worth blocking a service over. Start clean.
        }

        return new DailyState
        {
            Date = today,
            BootedAt = booted,
            SessionStartedAt = session,
            ServiceKey = serviceKey,
        };
    }

    /// <summary>
    /// True when the saved ticks belong to a different service. State written before this
    /// existed has no key, which counts as a match rather than losing someone's work.
    /// </summary>
    private static bool IsDifferentService(DailyState state, string? serviceKey) =>
        serviceKey is not null
        && state.ServiceKey is not null
        && !string.Equals(state.ServiceKey, serviceKey, StringComparison.Ordinal);

    /// <summary>
    /// True when the PC has been off and on again since this state was written — either a
    /// real reboot, or a Fast Startup power cycle that only the logon session reveals.
    /// <para>
    /// State from before these stamps existed has neither; that counts as no restart rather
    /// than throwing away an operator's work the first time they update.
    /// </para>
    /// </summary>
    private bool HasRestarted(DailyState state, DateTimeOffset booted, DateTimeOffset? session)
    {
        if (_resetMode != Models.ResetModes.PowerCycle)
        {
            return false;
        }

        if (state.BootedAt is { } previousBoot && (booted - previousBoot).Duration() > BootTolerance)
        {
            return true;
        }

        return state.SessionStartedAt is { } previousSession
               && session is { } currentSession
               && (currentSession - previousSession).Duration() > BootTolerance;
    }

    public void Save(DailyState state)
    {
        try
        {
            AppPaths.EnsureDataDirectories();

            // Re-stamped on every save so the file always describes the boot and session it
            // belongs to.
            state.BootedAt = BootTime();
            state.SessionStartedAt = SessionStartTime();

            File.WriteAllText(_path, JsonSerializer.Serialize(state, ChecklistLoader.JsonOptions));
        }
        catch (Exception)
        {
            // Losing persistence is survivable; the in-memory checklist keeps working.
        }
    }
}
