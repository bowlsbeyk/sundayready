using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using SundayReady.Models;

namespace SundayReady.Services;

/// <summary>
/// Passes when the configured host is reachable.
/// <para>
/// Implemented rather than stubbed because the top bar's connectivity pill reads from it —
/// leaving it stubbed would mean the pill could only ever show an unknown state. Tries ICMP
/// first and falls back to a TCP connect on 443, because plenty of church networks drop
/// outbound ping from the booth VLAN while allowing ordinary HTTPS.
/// </para>
/// </summary>
public sealed class InternetReachableVerifier : IVerifier
{
    public const string DefaultHost = "1.1.1.1";

    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TcpTimeout = TimeSpan.FromSeconds(2);

    public string Kind => "internetReachable";

    public string Describe(VerifySpec spec) => $"internetReachable · {spec.Host ?? DefaultHost}";

    public async Task<VerifyOutcome> CheckAsync(VerifySpec spec, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var host = string.IsNullOrWhiteSpace(spec.Host) ? DefaultHost : spec.Host;

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, (int)PingTimeout.TotalMilliseconds).ConfigureAwait(true);
            if (reply.Status == IPStatus.Success)
            {
                return VerifyOutcome.Pass($"{reply.RoundtripTime} ms", Stopwatch.GetElapsedTime(started));
            }
        }
        catch (Exception)
        {
            // ICMP blocked or unavailable — fall through to the TCP probe rather than
            // reporting the network down on the strength of a firewall rule.
        }

        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TcpTimeout);

            await client.ConnectAsync(host, 443, timeout.Token).ConfigureAwait(true);

            var elapsed = Stopwatch.GetElapsedTime(started);
            return VerifyOutcome.Pass($"{elapsed.TotalMilliseconds:0} ms via tcp/443", elapsed);
        }
        catch (Exception ex)
        {
            return VerifyOutcome.Fail($"{host} unreachable — {ex.Message}", Stopwatch.GetElapsedTime(started));
        }
    }
}
