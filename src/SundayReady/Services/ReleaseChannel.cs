namespace SundayReady.Services;

/// <summary>
/// How far ahead of stable a station is willing to run. A channel is a risk tolerance, not a
/// separate line of development: there is one history of releases and a channel says how early
/// in that history this PC is happy to pick them up.
/// <para>
/// So a station on <c>beta</c> takes beta and production releases but never an alpha, and the
/// booth PC that runs the 10:30 service stays on <c>production</c> while a spare machine sits
/// on <c>dev</c> and finds the problems first.
/// </para>
/// </summary>
public enum ReleaseChannel
{
    /// <summary>Anything tagged, including throwaway builds. For a machine nobody depends on.</summary>
    Dev = 0,

    /// <summary>Feature-complete but barely used. Expect to find things.</summary>
    Alpha = 1,

    /// <summary>Believed good, wants real Sundays behind it before it goes out to everyone.</summary>
    Beta = 2,

    /// <summary>The default, and where every station should sit unless it is a test machine.</summary>
    Production = 3,
}

/// <summary>
/// A release version and the channel its tag puts it in. Two of these compare in the order the
/// builds were actually cut — <c>1.3.0-dev.1</c>, then <c>-alpha.1</c>, then <c>-beta.1</c>, then
/// plain <c>1.3.0</c> — which is what lets the updater pick "the newest one I am allowed to run".
/// </summary>
public sealed record ReleaseVersion(Version Version, ReleaseChannel Channel, int Sequence)
    : IComparable<ReleaseVersion>
{
    /// <summary>Tag form, without the leading <c>v</c>: <c>1.3.0</c> or <c>1.3.0-beta.2</c>.</summary>
    public string Text => Channel == ReleaseChannel.Production
        ? Version.ToString(3)
        : $"{Version.ToString(3)}-{Suffix(Channel)}.{Sequence}";

    /// <summary>What the tag would be: <c>v1.3.0-beta.2</c>.</summary>
    public string Tag => $"v{Text}";

    public bool IsPrerelease => Channel != ReleaseChannel.Production;

    /// <summary>True when a station on <paramref name="channel"/> is willing to run this build.</summary>
    public bool IsOffered(ReleaseChannel channel) => Channel >= channel;

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byVersion = Version.CompareTo(other.Version);
        if (byVersion != 0)
        {
            return byVersion;
        }

        // Same 1.3.0, different stage of getting there. Production last, because it is the
        // build the prereleases were leading up to.
        var byChannel = Channel.CompareTo(other.Channel);
        return byChannel != 0 ? byChannel : Sequence.CompareTo(other.Sequence);
    }

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Parses <c>v1.3.0</c>, <c>1.3.0-beta.2</c> or <c>v1.3.0-dev</c>. Returns null for anything
    /// that is not a release tag, so a stray tag in the repository is ignored rather than
    /// mistaken for an update.
    /// </summary>
    public static ReleaseVersion? Parse(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var text = tag.Trim().TrimStart('v', 'V');

        // Build metadata carries no precedence, so it is dropped rather than parsed.
        if (text.IndexOf('+') is var plus && plus >= 0)
        {
            text = text[..plus];
        }

        var dash = text.IndexOf('-');
        var numbers = dash < 0 ? text : text[..dash];
        var suffix = dash < 0 ? string.Empty : text[(dash + 1)..];

        if (!Version.TryParse(numbers, out var parsed) || parsed.Major < 0 || parsed.Minor < 0)
        {
            return null;
        }

        var version = new Version(parsed.Major, parsed.Minor, parsed.Build < 0 ? 0 : parsed.Build);

        if (suffix.Length == 0)
        {
            return new ReleaseVersion(version, ReleaseChannel.Production, 0);
        }

        var parts = suffix.Split('.', 2);
        if (!TryParseChannel(parts[0], out var channel) || channel == ReleaseChannel.Production)
        {
            return null;
        }

        // "-beta" with no number is beta.0, so hand-cut tags still work.
        var sequence = parts.Length > 1 && int.TryParse(parts[1], out var n) && n >= 0 ? n : 0;
        return new ReleaseVersion(version, channel, sequence);
    }

    public static bool TryParseChannel(string? value, out ReleaseChannel channel)
    {
        channel = ReleaseChannel.Production;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "production":
            case "prod":
            case "stable":
            case "release":
                channel = ReleaseChannel.Production;
                return true;
            case "beta":
                channel = ReleaseChannel.Beta;
                return true;
            case "alpha":
                channel = ReleaseChannel.Alpha;
                return true;
            case "dev":
            case "development":
            case "nightly":
                channel = ReleaseChannel.Dev;
                return true;
            default:
                return false;
        }
    }

    /// <summary>The tag suffix for a channel, and the value stored in <c>station.json</c>.</summary>
    public static string Suffix(ReleaseChannel channel) => channel switch
    {
        ReleaseChannel.Beta => "beta",
        ReleaseChannel.Alpha => "alpha",
        ReleaseChannel.Dev => "dev",
        _ => "production",
    };

    /// <summary>Settings-screen order: safest first.</summary>
    public static IReadOnlyList<ReleaseChannel> All { get; } = new[]
    {
        ReleaseChannel.Production,
        ReleaseChannel.Beta,
        ReleaseChannel.Alpha,
        ReleaseChannel.Dev,
    };

    public static string Describe(ReleaseChannel channel) => channel switch
    {
        ReleaseChannel.Beta => "Beta — releases that want a few real Sundays before going out to everyone.",
        ReleaseChannel.Alpha => "Alpha — feature-complete but barely used. Expect to find things.",
        ReleaseChannel.Dev => "Dev — every build, including throwaway ones. For a machine nobody depends on.",
        // Production, and anything unrecognised: the safe reading of a value we do not know.
        _ => "Production — only finished releases. Where booth machines belong.",
    };
}
