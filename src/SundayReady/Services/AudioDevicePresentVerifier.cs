using System.Text.Json;
using SundayReady.Models;

namespace SundayReady.Services;

/// <summary>
/// Passes when an active audio device whose name contains the configured substring is present.
/// <para>
/// This is the "is the Focusrite actually plugged in" check — the one that catches the USB lead
/// somebody borrowed on Wednesday. Windows is enumerated from the MMDevices registry keys that
/// Core Audio itself maintains, which needs no COM interop and no audio packages; macOS asks
/// <c>system_profiler</c> for its JSON audio inventory. Both paths fail with the device names
/// they DID see, because "no device called X · saw: Y, Z" is a sentence somebody can act on and
/// "check failed" is not.
/// </para>
/// </summary>
public sealed class AudioDevicePresentVerifier : IVerifier
{
    public string Kind => "audioDevicePresent";

    public string Describe(VerifySpec spec) => $"audioDevicePresent \"{spec.NameContains}\"";

    public async Task<VerifyOutcome> CheckAsync(VerifySpec spec, CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(spec.NameContains))
        {
            return VerifyOutcome.Fail("audioDevicePresent needs nameContains — the text to look for in the device's name", started.Elapsed);
        }

        List<string> names;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                names = WindowsAudioDeviceNames();
            }
            else if (OperatingSystem.IsMacOS())
            {
                names = await MacAudioDeviceNamesAsync(cancellationToken);
            }
            else
            {
                return VerifyOutcome.Fail("audio device enumeration is not supported on this platform", started.Elapsed);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return VerifyOutcome.Fail($"could not list audio devices: {ex.Message}", started.Elapsed);
        }

        var match = names.FirstOrDefault(n => n.Contains(spec.NameContains, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            return VerifyOutcome.Pass($"found \"{match}\"", started.Elapsed);
        }

        var seen = names.Count == 0
            ? "no active audio devices at all"
            : "saw: " + string.Join(", ", names.Distinct().Take(4));

        return VerifyOutcome.Fail($"no audio device containing \"{spec.NameContains}\" · {seen}", started.Elapsed);
    }

    /// <summary>
    /// Active render and capture endpoints from the MMDevices registry — the same store the
    /// Core Audio device enumerator reads. DeviceState 1 is ACTIVE; unplugged and disabled
    /// devices carry other flags and are skipped, because "the interface is listed but dead"
    /// must not pass a presence check.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static List<string> WindowsAudioDeviceNames()
    {
        var names = new List<string>();

        // Verified against a real machine's registry, not documentation: Sound Settings shows
        // "Speakers (Realtek(R) Audio)", composed from the device description (the a45c... pid 2
        // property) and the interface's friendly name (the b3f8fa53... pid 6). Both parts are
        // matched, plus the composed form, because people type whichever one they have read.
        const string DescriptionValue = "{a45c254e-df1c-4efd-8020-67d146a850e0},2";
        const string InterfaceValue = "{b3f8fa53-0004-438e-9003-51a46e139bfc},6";

        foreach (var flow in new[] { "Render", "Capture" })
        {
            using var root = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\" + flow);

            if (root is null)
            {
                continue;
            }

            foreach (var id in root.GetSubKeyNames())
            {
                using var device = root.OpenSubKey(id);

                // DeviceState carries transport flags in the high bits; the low nibble is the
                // state, and 1 is ACTIVE. Testing equality against 1 sees zero devices on a
                // real PC - found the hard way on this one.
                if (device?.GetValue("DeviceState") is not int state || (state & 0xF) != 1)
                {
                    continue;
                }

                using var properties = device.OpenSubKey("Properties");
                var description = properties?.GetValue(DescriptionValue) as string;
                var iface = properties?.GetValue(InterfaceValue) as string;

                if (!string.IsNullOrWhiteSpace(description) && !string.IsNullOrWhiteSpace(iface))
                {
                    names.Add(description + " (" + iface + ")");
                }
                else if (!string.IsNullOrWhiteSpace(description))
                {
                    names.Add(description);
                }
                else if (!string.IsNullOrWhiteSpace(iface))
                {
                    names.Add(iface);
                }
            }
        }

        return names;
    }

    /// <summary>macOS: <c>system_profiler -json SPAudioDataType</c>, names from the item tree.</summary>
    private static async Task<List<string>> MacAudioDeviceNamesAsync(CancellationToken cancellationToken)
    {
        var info = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "/usr/sbin/system_profiler",
            Arguments = "-json SPAudioDataType",
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        using var process = System.Diagnostics.Process.Start(info)
            ?? throw new InvalidOperationException("system_profiler would not start");

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var names = new List<string>();
        using var json = JsonDocument.Parse(output);

        if (json.RootElement.TryGetProperty("SPAudioDataType", out var groups))
        {
            foreach (var group in groups.EnumerateArray())
            {
                if (!group.TryGetProperty("_items", out var items))
                {
                    continue;
                }

                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("_name", out var name) && name.GetString() is { Length: > 0 } text)
                    {
                        names.Add(text);
                    }
                }
            }
        }

        return names;
    }
}
