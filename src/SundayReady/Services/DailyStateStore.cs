using System.Text.Json;

namespace SundayReady.Services;

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

    /// <summary>Keyed by <see cref="DailyStateStore.KeyFor"/>.</summary>
    public Dictionary<string, ItemState> Items { get; set; } = new();

    public string? OperatorInitials { get; set; }

    public DateTimeOffset? SignedOffAt { get; set; }

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
    private readonly bool _resetOnRestart;

    public DailyStateStore(string? path = null, bool resetOnRestart = true)
    {
        _path = path ?? AppPaths.StateFile;
        _resetOnRestart = resetOnRestart;
    }

    /// <summary>
    /// When Windows last booted. Environment.TickCount64 is milliseconds of uptime, so the
    /// wall clock minus uptime is the moment of boot — stable across app restarts, unlike a
    /// process start time, which is exactly the distinction that matters here.
    /// </summary>
    public static DateTimeOffset BootTime() =>
        DateTimeOffset.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>
    /// Identifies an item across runs. Scoped by source file so two tabs can carry the same
    /// label. Editing a label in the JSON drops that item's tick for the rest of the day — an
    /// acceptable trade for not needing hand-maintained ids in the checklist files.
    /// </summary>
    public static string KeyFor(string sourceFile, string label) => $"{sourceFile}|{label}";

    public DailyState Load(DateOnly today)
    {
        var booted = BootTime();

        try
        {
            if (File.Exists(_path))
            {
                var state = JsonSerializer.Deserialize<DailyState>(File.ReadAllText(_path), ChecklistLoader.JsonOptions);

                if (state is not null && state.Date == today && !HasRebooted(state, booted))
                {
                    return state;
                }
            }
        }
        catch (Exception)
        {
            // A corrupt state file is not worth blocking a service over. Start clean.
        }

        return new DailyState { Date = today, BootedAt = booted };
    }

    /// <summary>
    /// True when the PC has restarted since this state was written. State from before the
    /// feature existed has no boot stamp; that counts as the same boot rather than throwing
    /// away an operator's work the first time they update.
    /// </summary>
    private bool HasRebooted(DailyState state, DateTimeOffset booted) =>
        _resetOnRestart
        && state.BootedAt is { } previous
        && (booted - previous).Duration() > BootTolerance;

    public void Save(DailyState state)
    {
        try
        {
            AppPaths.EnsureDataDirectories();

            // Re-stamped on every save so the file always describes the boot it belongs to.
            state.BootedAt = BootTime();

            File.WriteAllText(_path, JsonSerializer.Serialize(state, ChecklistLoader.JsonOptions));
        }
        catch (Exception)
        {
            // Losing persistence is survivable; the in-memory checklist keeps working.
        }
    }
}
