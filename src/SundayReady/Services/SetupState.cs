using System.Text.Json;

namespace SundayReady.Services;

/// <summary>Records that someone has been through first-time setup on this machine.</summary>
public sealed class SetupRecord
{
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>True when the walkthrough was dismissed rather than finished.</summary>
    public bool Skipped { get; set; }

    /// <summary>Which build ran it, for when the walkthrough itself changes.</summary>
    public string? Version { get; set; }
}

/// <summary>
/// Whether this machine has been set up, which is what decides if the walkthrough opens.
/// <para>
/// It cannot be inferred from the files. A fresh install ships with sample checklists and a
/// <c>station.json</c>, so "no content" is not the same as "nobody has set this up" — and a
/// station whose files were copied from another PC still has a person in front of it who has
/// never seen the app. So the record lives in the per-user data directory instead: new machine
/// or new user, new walkthrough.
/// </para>
/// </summary>
public static class SetupState
{
    private static string Path => System.IO.Path.Combine(AppPaths.DataDirectory, "setup.json");

    public static SetupRecord? Read()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize<SetupRecord>(File.ReadAllText(Path), ChecklistLoader.JsonOptions)
                : null;
        }
        catch (Exception)
        {
            // An unreadable record reads as "not set up". Offering the walkthrough again is a
            // far smaller problem than never offering it.
            return null;
        }
    }

    /// <summary>True when nobody has finished or dismissed the walkthrough on this machine.</summary>
    public static bool NeedsWalkthrough => Read() is null;

    public static void MarkDone(bool skipped)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(
                Path,
                JsonSerializer.Serialize(
                    new SetupRecord
                    {
                        CompletedAt = DateTimeOffset.Now,
                        Skipped = skipped,
                        Version = AppVersion.Current.Text,
                    },
                    ChecklistWriter.WriteOptions));
        }
        catch (Exception ex)
        {
            // Worst case the walkthrough opens again next launch, which is survivable.
            UpdateInstaller.Log($"could not record setup completion: {ex.Message}");
        }
    }

    /// <summary>Forgets the record, so the walkthrough can be run again from Settings.</summary>
    public static void Clear()
    {
        try
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch (Exception)
        {
            // Nothing useful to do; the caller opens the walkthrough directly anyway.
        }
    }
}
