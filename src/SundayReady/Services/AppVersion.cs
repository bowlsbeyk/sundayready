using System.Reflection;

namespace SundayReady.Services;

/// <summary>
/// The running build's version. Stamped by the csproj (and overridden by the release
/// workflow from the pushed tag), never hand-typed into the UI.
/// </summary>
public static class AppVersion
{
    public static Version Current { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version is { } version
            ? new Version(version.Major, version.Minor, version.Build < 0 ? 0 : version.Build)
            : new Version(0, 0, 0);

    /// <summary>Top-bar form: <c>v0.4.0</c>.</summary>
    public static string Display => $"v{Current.ToString(3)}";

    /// <summary>Parses a release tag such as <c>v0.5.1</c>. Returns null if it is not a version.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var trimmed = tag.TrimStart('v', 'V');
        return Version.TryParse(trimmed, out var parsed)
            ? new Version(parsed.Major, parsed.Minor, parsed.Build < 0 ? 0 : parsed.Build)
            : null;
    }
}
