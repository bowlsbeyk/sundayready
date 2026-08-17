namespace SundayReady.Services;

/// <summary>
/// Where the app reads and writes.
/// <para>
/// State and logs go under the user's local data directory, because a booth PC running this out
/// of Program Files cannot write to its own folder.
/// </para>
/// <para>
/// Content — the checklists and <c>station.json</c> — is read from next to the executable on
/// Windows, where an update replaces only the .exe and leaves everything beside it alone. On
/// macOS it cannot be: the unit an update replaces there is the whole <c>.app</c>, so anything
/// kept inside the bundle would be destroyed by the next update. Mac stations therefore keep
/// their content in the data directory, seeded from the bundle the first time the app runs.
/// </para>
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// State, logs, and on macOS the station's content.
    /// <para>
    /// Spelled out on macOS rather than taken from <c>SpecialFolder.LocalApplicationData</c>,
    /// which .NET maps to <c>~/.local/share</c> there — correct for Unix, but not where any Mac
    /// user would look, and this is a folder people are told to open.
    /// </para>
    /// </summary>
    public static string DataDirectory => Path.Combine(DataRoot, "SundayReady");

    private static string DataRoot => OperatingSystem.IsMacOS()
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Application Support")
        : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>The content that ships with the build, and the source for <see cref="SeedContent"/>.</summary>
    public static string ShippedContentDirectory => AppContext.BaseDirectory;

    /// <summary>
    /// Where checklists and <c>station.json</c> actually live for this install. Editing a
    /// checklist writes here, so it has to be somewhere an update will not overwrite.
    /// </summary>
    public static string ContentDirectory =>
        AppPlatform.Layout == InstallLayout.AppBundle ? DataDirectory : ShippedContentDirectory;

    public static string ChecklistsDirectory => Path.Combine(ContentDirectory, "checklists");

    public static string StationConfigFile => Path.Combine(ContentDirectory, StationConfigLoader.FileName);

    public static string StateFile => Path.Combine(DataDirectory, "state.json");

    public static string LogsDirectory => Path.Combine(DataDirectory, "logs");

    public static string UpdatesDirectory => Path.Combine(DataDirectory, "updates");

    /// <summary>
    /// Local stand-in for the techdesk share, used until <c>station.json</c> names a real one.
    /// Everything works the same way; the techdesk just only sees this PC.
    /// </summary>
    public static string TechdeskDirectory => Path.Combine(DataDirectory, "techdesk");

    public static string PendingUpdateFile => Path.Combine(UpdatesDirectory, "pending.json");

    public static void EnsureDataDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    /// <summary>
    /// Copies the shipped checklists and <c>station.json</c> into <see cref="ContentDirectory"/>
    /// when that is somewhere else and does not have them yet — a first run on macOS, in other
    /// words. Existing files are never overwritten: after the first run this station's content is
    /// its own, and a sample from a newer build has no business replacing it.
    /// </summary>
    public static void SeedContent()
    {
        if (string.Equals(
                Path.TrimEndingDirectorySeparator(ContentDirectory),
                Path.TrimEndingDirectorySeparator(ShippedContentDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(ContentDirectory);

            var shippedConfig = Path.Combine(ShippedContentDirectory, StationConfigLoader.FileName);
            if (File.Exists(shippedConfig) && !File.Exists(StationConfigFile))
            {
                File.Copy(shippedConfig, StationConfigFile);
            }

            var shippedChecklists = Path.Combine(ShippedContentDirectory, "checklists");
            if (!Directory.Exists(shippedChecklists))
            {
                return;
            }

            Directory.CreateDirectory(ChecklistsDirectory);
            foreach (var source in Directory.EnumerateFiles(shippedChecklists, "*.json"))
            {
                var destination = Path.Combine(ChecklistsDirectory, Path.GetFileName(source));
                if (!File.Exists(destination))
                {
                    File.Copy(source, destination);
                }
            }
        }
        catch (Exception ex)
        {
            // Nothing to escalate to: the app carries on and shows whatever content it can
            // find, which is the same thing it does for a station with no checklists yet.
            UpdateInstaller.Log($"could not seed content into {ContentDirectory}: {ex.Message}");
        }
    }
}
