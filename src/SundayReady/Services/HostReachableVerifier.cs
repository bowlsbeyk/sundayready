using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using SundayReady.Models;

namespace SundayReady.Services;

/// <summary>
/// Passes when a device on the network answers. This is the one for cameras, encoders,
/// consoles and NDI boxes — anything with an address that either replies to ping or has a
/// port open.
/// <para>
/// Distinct from <see cref="InternetReachableVerifier"/>, which is about the WAN and falls
/// back to tcp/443. A camera has no 443; it has a web UI on 80, or nothing but ICMP.
/// </para>
/// <para>
/// Worth being clear about what this proves: the device is powered and on the network. It
/// does not prove the camera is pointed anywhere useful, in focus, or actually arriving in
/// vMix — for that, check the switcher's own input list with <c>httpContains</c>.
/// </para>
/// </summary>
public sealed class HostReachableVerifier : IVerifier
{
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    public string Kind => "hostReachable";

    public string Describe(VerifySpec spec) => spec.Port is > 0
        ? $"hostReachable · {spec.Host}:{spec.Port}"
        : $"hostReachable · {spec.Host}";

    public async Task<VerifyOutcome> CheckAsync(VerifySpec spec, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();

        if (string.IsNullOrWhiteSpace(spec.Host))
        {
            return VerifyOutcome.Fail("no host configured", Stopwatch.GetElapsedTime(started));
        }

        // A port turns this into a service check, which is the stronger statement: the device
        // is not just answering pings, something is listening on the thing you care about.
        if (spec.Port is > 0 and <= 65535)
        {
            try
            {
                using var client = new TcpClient();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ConnectTimeout);

                await client.ConnectAsync(spec.Host, spec.Port.Value, timeout.Token).ConfigureAwait(true);

                var elapsed = Stopwatch.GetElapsedTime(started);
                return VerifyOutcome.Pass($"{elapsed.TotalMilliseconds:0} ms on port {spec.Port}", elapsed);
            }
            catch (Exception ex)
            {
                return VerifyOutcome.Fail(
                    $"nothing listening on {spec.Host}:{spec.Port} — {Reason(ex)}",
                    Stopwatch.GetElapsedTime(started));
            }
        }

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(spec.Host, (int)PingTimeout.TotalMilliseconds).ConfigureAwait(true);

            return reply.Status == IPStatus.Success
                ? VerifyOutcome.Pass($"{reply.RoundtripTime} ms", Stopwatch.GetElapsedTime(started))
                : VerifyOutcome.Fail($"no reply — {reply.Status}", Stopwatch.GetElapsedTime(started));
        }
        catch (Exception ex)
        {
            return VerifyOutcome.Fail(Reason(ex), Stopwatch.GetElapsedTime(started));
        }
    }

    private static string Reason(Exception ex) => ex switch
    {
        OperationCanceledException => "timed out",
        // Ping wraps the real cause, and "an exception occurred during a Ping request" tells
        // an operator nothing. A bad name is the likeliest mistake, so say that.
        PingException { InnerException: SocketException } => "could not resolve that name",
        PingException => "could not send the ping — check the address",
        SocketException { SocketErrorCode: SocketError.HostNotFound } => "could not resolve that name",
        SocketException { SocketErrorCode: SocketError.ConnectionRefused } => "connection refused",
        SocketException socket => socket.SocketErrorCode.ToString(),
        _ => ex.Message,
    };
}
