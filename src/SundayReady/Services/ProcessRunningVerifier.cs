using System.Diagnostics;
using SundayReady.Models;

namespace SundayReady.Services;

/// <summary>Passes when a process with the configured name is running.</summary>
public sealed class ProcessRunningVerifier : IVerifier
{
    public string Kind => "processRunning";

    public string Describe(VerifySpec spec) => $"processRunning \"{spec.ProcessName}\"";

    public Task<VerifyOutcome> CheckAsync(VerifySpec spec, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        TimeSpan Elapsed() => Stopwatch.GetElapsedTime(started);

        var name = spec.ProcessName;
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult(VerifyOutcome.Fail("no processName configured", Elapsed()));
        }

        // GetProcessesByName wants the bare name, so tolerate "vMix64.exe" in the JSON.
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        try
        {
            var processes = Process.GetProcessesByName(name);
            foreach (var process in processes)
            {
                process.Dispose();
            }

            return Task.FromResult(processes.Length > 0
                ? VerifyOutcome.Pass($"{processes.Length} process(es) named {name}", Elapsed())
                : VerifyOutcome.Fail($"no process named {name} is running", Elapsed()));
        }
        catch (Exception ex)
        {
            // Enumerating processes can fail on a locked-down box. Treat it as "not verified".
            return Task.FromResult(VerifyOutcome.Fail($"could not enumerate processes — {ex.Message}", Elapsed()));
        }
    }
}
