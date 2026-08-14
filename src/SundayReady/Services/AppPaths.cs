namespace SundayReady.Services;

/// <summary>
/// Where the app reads and writes.
/// <para>
/// Checklists are read from next to the exe — they are content, deployed with the app and
/// possibly replaced from a share later. State and logs go under LOCALAPPDATA instead,
/// because a booth PC running this out of Program Files cannot write to its own folder.
/// </para>
/// </summary>
public static class AppPaths
{
    public static string ChecklistsDirectory =>
        Path.Combine(AppContext.BaseDirectory, "checklists");

    public static string DataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SundayReady");

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
}
