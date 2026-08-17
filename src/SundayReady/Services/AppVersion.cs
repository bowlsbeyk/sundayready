using System.Reflection;

namespace SundayReady.Services;

/// <summary>
/// The running build's version. Stamped by the csproj (and overridden by the release workflow
/// from the pushed tag), never hand-typed into the UI.
/// </summary>
public static class AppVersion
{
    /// <summary>
    /// Version and channel together, so a prerelease build knows it is one.
    /// <para>
    /// Read from <c>AssemblyInformationalVersion</c>, which is the only assembly attribute that
    /// can carry <c>-beta.2</c> — the numeric assembly version cannot. It falls back to the
    /// numeric version, which means a build with no informational version reads as production;
    /// that is the safe way round, since a station then only accepts finished releases.
    /// </para>
    /// </summary>
    public static ReleaseVersion Current { get; } = ResolveCurrent();

    /// <summary>Top-bar form: <c>v0.14.0</c>, or <c>v0.15.0-beta.2</c> on a prerelease.</summary>
    public static string Display => Current.Tag;

    /// <summary>The channel this build was cut on — the floor for what it will update to.</summary>
    public static ReleaseChannel Channel => Current.Channel;

    /// <summary>Parses a release tag such as <c>v0.5.1</c> or <c>v0.6.0-beta.1</c>.</summary>
    public static ReleaseVersion? ParseTag(string? tag) => ReleaseVersion.Parse(tag);

    private static ReleaseVersion ResolveCurrent()
    {
        var assembly = Assembly.GetEntryAssembly();

        var informational = assembly
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (ReleaseVersion.Parse(informational) is { } parsed)
        {
            return parsed;
        }

        var numeric = assembly?.GetName().Version;
        var version = numeric is null
            ? new Version(0, 0, 0)
            : new Version(numeric.Major, numeric.Minor, numeric.Build < 0 ? 0 : numeric.Build);

        return new ReleaseVersion(version, ReleaseChannel.Production, 0);
    }
}
