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
/// Everything about today's service that survives a restart. The checklist is per-service,
/// so this is thrown away wholesale on a new calendar day.
/// </summary>
public sealed class DailyState
{
    public DateOnly Date { get; set; }

    /// <summary>Keyed by <see cref="DailyStateStore.KeyFor"/>.</summary>
    public Dictionary<string, ItemState> Items { get; set; } = new();

    public string? OperatorInitials { get; set; }

    public DateTimeOffset? SignedOffAt { get; set; }

    /// <summary>True when the service was signed off with overridden or open items.</summary>
    public bool Partial { get; set; }
}

/// <summary>
/// Loads and saves <see cref="DailyState"/>, discarding it on a new calendar day, so Sunday's
/// ticks are not still showing on Wednesday.
/// </summary>
public sealed class DailyStateStore
{
    private readonly string _path;

    public DailyStateStore(string? path = null)
    {
        _path = path ?? AppPaths.StateFile;
    }

    /// <summary>
    /// Identifies an item across runs. Scoped by source file so two tabs can carry the same
    /// label. Editing a label in the JSON drops that item's tick for the rest of the day — an
    /// acceptable trade for not needing hand-maintained ids in the checklist files.
    /// </summary>
    public static string KeyFor(string sourceFile, string label) => $"{sourceFile}|{label}";

    public DailyState Load(DateOnly today)
    {
        try
        {
            if (File.Exists(_path))
            {
                var state = JsonSerializer.Deserialize<DailyState>(File.ReadAllText(_path), ChecklistLoader.JsonOptions);
                if (state is not null && state.Date == today)
                {
                    return state;
                }
            }
        }
        catch (Exception)
        {
            // A corrupt state file is not worth blocking a service over. Start clean.
        }

        return new DailyState { Date = today };
    }

    public void Save(DailyState state)
    {
        try
        {
            AppPaths.EnsureDataDirectories();
            File.WriteAllText(_path, JsonSerializer.Serialize(state, ChecklistLoader.JsonOptions));
        }
        catch (Exception)
        {
            // Losing persistence is survivable; the in-memory checklist keeps working.
        }
    }
}
