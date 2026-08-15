using System.Diagnostics;
using SundayReady.Models;

namespace SundayReady.Services;

/// <summary>
/// Passes when an NDI source whose name contains the given text is on the network.
/// <para>
/// NDI senders announce themselves over mDNS as <c>_ndi._tcp.local</c>, so this asks the
/// network the same question a switcher's "Add Input → NDI" list does — no NDI runtime to
/// install and nothing to license.
/// </para>
/// <para>
/// What it proves: the sender is powered, on the network, and advertising. It does not prove
/// the picture is any good, and it does not prove your switcher has actually taken the source
/// as an input — for that, ask the switcher with <c>httpContains</c>.
/// </para>
/// </summary>
public sealed class NdiSourceVerifier : IVerifier
{
    /// <summary>The service NDI senders advertise themselves under.</summary>
    public const string ServiceType = "_ndi._tcp.local";

    /// <summary>
    /// Long enough for responders to answer, short enough not to stall a five-second poll.
    /// </summary>
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(1500);

    public string Kind => "ndiSourcePresent";

    public string Describe(VerifySpec spec) => $"ndiSourcePresent \"{spec.NameContains}\"";

    public async Task<VerifyOutcome> CheckAsync(VerifySpec spec, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();

        if (string.IsNullOrWhiteSpace(spec.NameContains))
        {
            return VerifyOutcome.Fail("no source name configured", Stopwatch.GetElapsedTime(started));
        }

        var sources = await MulticastDns.BrowseAsync(ServiceType, Window, cancellationToken).ConfigureAwait(true);
        var elapsed = Stopwatch.GetElapsedTime(started);

        var match = sources.FirstOrDefault(s => s.Contains(spec.NameContains, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return VerifyOutcome.Pass($"found \"{match}\"", elapsed);
        }

        // Naming what *was* found is the whole diagnostic here: nine times out of ten the
        // source is up and the name in the checklist does not match what it calls itself.
        return sources.Count == 0
            ? VerifyOutcome.Fail("no NDI sources are announcing on this network", elapsed)
            : VerifyOutcome.Fail($"no match. On the network now: {string.Join(", ", sources.Take(6))}", elapsed);
    }
}
