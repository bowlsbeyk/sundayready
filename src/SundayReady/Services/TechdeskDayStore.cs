using System.Text.Json;

namespace SundayReady.Services;

/// <summary>
/// The techdesk's own decisions for today — currently just which stations the tech director
/// has accepted are not staffed, so a dark booth stops shouting at them for the rest of the
/// morning. Thrown away on a new calendar day, like the station's checked state.
/// </summary>
public sealed class TechdeskDay
{
    public DateOnly Date { get; set; }

    /// <summary>Station keys, as written by <see cref="TechdeskDayStore.KeyFor"/>.</summary>
    public List<string> NotStaffed { get; set; } = new();
}

public sealed class TechdeskDayStore
{
    private readonly string _path;

    public TechdeskDayStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.DataDirectory, "techdesk-day.json");
    }

    public static string KeyFor(string host, string station) =>
        (string.IsNullOrWhiteSpace(host) ? station : host).ToUpperInvariant();

    public TechdeskDay Load(DateOnly today)
    {
        try
        {
            if (File.Exists(_path))
            {
                var day = JsonSerializer.Deserialize<TechdeskDay>(File.ReadAllText(_path), ChecklistLoader.JsonOptions);
                if (day is not null && day.Date == today)
                {
                    return day;
                }
            }
        }
        catch (Exception)
        {
            // Same trade as the station's state file: start clean rather than block a service.
        }

        return new TechdeskDay { Date = today };
    }

    public void Save(TechdeskDay day)
    {
        try
        {
            AppPaths.EnsureDataDirectories();
            File.WriteAllText(_path, JsonSerializer.Serialize(day, ChecklistLoader.JsonOptions));
        }
        catch (Exception)
        {
            // Losing this costs one re-click after a restart.
        }
    }
}
