using System.Diagnostics;
using SundayReady.Models;

namespace SundayReady.Services;

/// <summary>Passes when the configured path exists on disk. Files and directories both count.</summary>
public sealed class FileExistsVerifier : IVerifier
{
    public string Kind => "fileExists";

    public string Describe(VerifySpec spec) => $"fileExists · {spec.Path}";

    public Task<VerifyOutcome> CheckAsync(VerifySpec spec, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        TimeSpan Elapsed() => Stopwatch.GetElapsedTime(started);

        if (string.IsNullOrWhiteSpace(spec.Path))
        {
            return Task.FromResult(VerifyOutcome.Fail("no path configured", Elapsed()));
        }

        try
        {
            // Lets a checklist say %USERPROFILE%\Desktop\... instead of hard-coding an operator's name.
            var path = Environment.ExpandEnvironmentVariables(spec.Path);

            if (File.Exists(path))
            {
                return Task.FromResult(VerifyOutcome.Pass($"file present, {new FileInfo(path).Length} bytes", Elapsed()));
            }

            if (Directory.Exists(path))
            {
                var count = Directory.EnumerateFileSystemEntries(path).Count();
                return Task.FromResult(VerifyOutcome.Pass($"folder present, {count} entries", Elapsed()));
            }

            return Task.FromResult(VerifyOutcome.Fail("path does not exist", Elapsed()));
        }
        catch (Exception ex)
        {
            return Task.FromResult(VerifyOutcome.Fail(ex.Message, Elapsed()));
        }
    }
}
