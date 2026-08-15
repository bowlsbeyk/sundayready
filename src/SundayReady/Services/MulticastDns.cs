using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SundayReady.Services;

/// <summary>
/// A one-shot mDNS query, just enough of it to ask "who is advertising this service?".
/// <para>
/// Hand-rolled rather than taking a dependency: this needs one query type (PTR) and one
/// record type back, and a booth PC should not gain a NuGet package for that.
/// </para>
/// </summary>
public static class MulticastDns
{
    private const int MulticastPort = 5353;

    private static readonly IPAddress MulticastGroup = IPAddress.Parse("224.0.0.251");

    /// <summary>
    /// Asks the local network which instances advertise a service, e.g. <c>_ndi._tcp.local</c>,
    /// and returns their instance names.
    /// </summary>
    public static async Task<IReadOnlyList<string>> BrowseAsync(
        string serviceType,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var socket = new UdpClient(AddressFamily.InterNetwork);

        try
        {
            // Sharing the port matters: Windows almost always has Bonjour or another responder
            // already listening on 5353, and an exclusive bind would simply fail.
            socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.ExclusiveAddressUse = false;
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, MulticastPort));

            try
            {
                socket.JoinMulticastGroup(MulticastGroup);
            }
            catch (SocketException)
            {
                // No multicast-capable interface. The query still goes out; some responders
                // answer by unicast anyway.
            }

            var query = BuildPtrQuery(serviceType);
            await socket.SendAsync(query, query.Length, new IPEndPoint(MulticastGroup, MulticastPort))
                .ConfigureAwait(false);

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);

            while (!deadline.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                foreach (var name in ReadPtrAnswers(result.Buffer, serviceType))
                {
                    found.Add(name);
                }
            }
        }
        catch (Exception)
        {
            // Firewall, no network, or the port could not be shared. An empty list is the
            // honest answer — the verifier reports "none found" rather than inventing one.
        }

        return found.ToList();
    }

    private static byte[] BuildPtrQuery(string serviceType)
    {
        var packet = new List<byte>
        {
            0, 0,          // transaction id — mDNS responders match on the question, not this
            0, 0,          // flags: standard query
            0, 1,          // one question
            0, 0, 0, 0, 0, 0,
        };

        WriteName(packet, serviceType);
        packet.AddRange(new byte[] { 0, 12 });   // QTYPE = PTR
        packet.AddRange(new byte[] { 0, 1 });    // QCLASS = IN

        return packet.ToArray();
    }

    private static void WriteName(List<byte> packet, string name)
    {
        foreach (var label in name.Trim('.').Split('.'))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            packet.Add((byte)bytes.Length);
            packet.AddRange(bytes);
        }

        packet.Add(0);
    }

    /// <summary>
    /// Pulls the instance names out of the PTR answers for the service we asked about, and
    /// ignores everything else on the wire — mDNS is a shared bus and most of the traffic
    /// belongs to something else.
    /// </summary>
    private static IEnumerable<string> ReadPtrAnswers(byte[] packet, string serviceType)
    {
        var names = new List<string>();

        try
        {
            if (packet.Length < 12)
            {
                return names;
            }

            var questions = (packet[4] << 8) | packet[5];
            var answers = (packet[6] << 8) | packet[7];
            var offset = 12;

            for (var i = 0; i < questions; i++)
            {
                ReadName(packet, ref offset);
                offset += 4;
            }

            var suffix = "." + serviceType.Trim('.');

            for (var i = 0; i < answers && offset < packet.Length; i++)
            {
                var owner = ReadName(packet, ref offset);
                if (offset + 10 > packet.Length)
                {
                    break;
                }

                var type = (packet[offset] << 8) | packet[offset + 1];
                var length = (packet[offset + 8] << 8) | packet[offset + 9];
                offset += 10;

                var rdataStart = offset;

                if (type == 12 && owner.TrimEnd('.').EndsWith(serviceType.Trim('.'), StringComparison.OrdinalIgnoreCase))
                {
                    var target = ReadName(packet, ref offset).TrimEnd('.');

                    // "CAM3 (Channel 1)._ndi._tcp.local" — the part before the service is the
                    // name a human gave the source.
                    if (target.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        target = target[..^suffix.Length];
                    }

                    if (target.Length > 0)
                    {
                        names.Add(target);
                    }
                }

                offset = rdataStart + length;
            }
        }
        catch (Exception)
        {
            // A malformed or truncated packet is normal on a busy network; take what parsed.
        }

        return names;
    }

    /// <summary>Reads a DNS name, following the compression pointers the format is full of.</summary>
    private static string ReadName(byte[] packet, ref int offset)
    {
        var labels = new List<string>();
        var jumped = false;
        var guard = 0;

        while (offset < packet.Length && guard++ < 128)
        {
            var length = packet[offset];

            if (length == 0)
            {
                offset++;
                break;
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (offset + 1 >= packet.Length)
                {
                    break;
                }

                var pointer = ((length & 0x3F) << 8) | packet[offset + 1];

                if (!jumped)
                {
                    // Only the first jump advances the caller's cursor; the rest is a detour.
                    offset += 2;
                    jumped = true;
                }

                var target = pointer;
                labels.Add(ReadName(packet, ref target));
                break;
            }

            offset++;
            if (offset + length > packet.Length)
            {
                break;
            }

            labels.Add(Encoding.UTF8.GetString(packet, offset, length));
            offset += length;
        }

        return string.Join('.', labels.Where(l => l.Length > 0));
    }
}
