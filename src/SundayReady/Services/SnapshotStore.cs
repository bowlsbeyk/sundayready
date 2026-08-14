using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using SundayReady.Models;

namespace SundayReady.Services;

/// <summary>
/// The techdesk transport: a folder every station can write to and the techdesk can read.
/// <para>
/// One file per station, replaced wholesale each heartbeat. There is no protocol to get
/// wrong, a station that never starts simply has no file, and the whole thing can be
/// inspected with Explorer on a Sunday morning when something is off.
/// </para>
/// </summary>
public sealed class SnapshotStore
{
    /// <summary>How often a station republishes. The techdesk sweeps on the same beat.</summary>
    public static readonly TimeSpan PublishInterval = TimeSpan.FromSeconds(15);

    private static readonly Lazy<string?> LocalAddress = new(DetectAddress);

    private readonly string _directory;

    public SnapshotStore(string? share = null)
    {
        _directory = Resolve(share);
    }

    public string Directory => _directory;

    /// <summary>
    /// The configured share, or a local folder when there is none. The local fallback means
    /// techdesk mode is testable on one PC before anyone has decided on a UNC path.
    /// </summary>
    public static string Resolve(string? share) =>
        string.IsNullOrWhiteSpace(share)
            ? AppPaths.TechdeskDirectory
            : Environment.ExpandEnvironmentVariables(share.Trim());

    /// <summary>
    /// Writes this station's file. Failure is swallowed on purpose: a share that is down, or
    /// a booth PC that booted before the network settled, must not disturb the operator —
    /// the techdesk will show the station as silent, which is the truth.
    /// </summary>
    public bool Publish(StationSnapshot snapshot)
    {
        try
        {
            System.IO.Directory.CreateDirectory(_directory);

            var path = Path.Combine(_directory, FileNameFor(snapshot));
            var temporary = path + ".tmp";

            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, ChecklistLoader.JsonOptions));

            // Replace rather than truncate-and-write, so the techdesk never reads a half file.
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Every snapshot currently in the share, newest heartbeat first. A file that will not
    /// parse is skipped rather than failing the sweep — one bad station must not blank the board.
    /// </summary>
    public IReadOnlyList<StationSnapshot> ReadAll()
    {
        if (!System.IO.Directory.Exists(_directory))
        {
            return Array.Empty<StationSnapshot>();
        }

        var snapshots = new List<StationSnapshot>();

        try
        {
            foreach (var path in System.IO.Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var snapshot = JsonSerializer.Deserialize<StationSnapshot>(File.ReadAllText(path), ChecklistLoader.JsonOptions);
                    if (snapshot is not null && !string.IsNullOrWhiteSpace(snapshot.Station))
                    {
                        snapshots.Add(snapshot);
                    }
                }
                catch (Exception)
                {
                    // Caught mid-write, or written by a build that has moved on. Skip it.
                }
            }
        }
        catch (Exception)
        {
            // The share went away between the existence check and the walk.
        }

        return snapshots.OrderBy(s => s.Station, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// One file per PC. Keyed on hostname rather than station name because the hostname is
    /// what cannot collide, and a station renamed mid-morning should replace its own file
    /// rather than appear twice on the board.
    /// </summary>
    private static string FileNameFor(StationSnapshot snapshot)
    {
        var host = string.IsNullOrWhiteSpace(snapshot.Host) ? snapshot.Station : snapshot.Host;
        var safe = new string(host.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());
        return $"{safe.ToLowerInvariant()}.json";
    }

    /// <summary>This station's IPv4 address, read off the adapters rather than by dialling out.</summary>
    public static string? Address => LocalAddress.Value;

    private static string? DetectAddress()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                    && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Select(address => address.Address)
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork)
                ?.ToString();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
