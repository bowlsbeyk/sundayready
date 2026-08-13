using System.Diagnostics;

namespace SundayReady.Services;

/// <summary>
/// Swaps a staged update in at launch, before any UI exists.
/// <para>
/// The swap is done by a short-lived PowerShell helper rather than by this process, because
/// a self-contained single-file build resolves assemblies lazily out of its own exe. Move or
/// rename that exe while it is running and the next assembly it needs is simply gone — which
/// is exactly what happens when you try to relaunch after replacing yourself. So this process
/// starts the helper while it is still intact, then exits; the helper waits for it to go,
/// moves the new build into place, and starts it.
/// </para>
/// </summary>
public static class UpdateInstaller
{
    private const int MaxFailedAttempts = 3;

    private const string BackupSuffix = ".old";

    /// <summary>
    /// Returns true when a swap has been handed off to the helper — the caller must exit
    /// immediately without initialising Avalonia, so the exe is free to be replaced.
    /// </summary>
    public static bool TryApply()
    {
        CleanupBackup();

        var pending = UpdateService.ReadPending();
        if (pending is null)
        {
            return false;
        }

        var staged = pending.File;
        var running = Environment.ProcessPath;

        if (running is null
            || !running.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(staged))
        {
            UpdateService.ClearPending();
            return false;
        }

        // Already on this version or newer. Either the last swap worked and this is the
        // clean-up pass, or the staged build is simply stale.
        if (AppVersion.ParseTag(pending.Version) is not { } version || version <= AppVersion.Current)
        {
            TryDelete(staged);
            UpdateService.ClearPending();
            Log($"cleared staged {pending.Version}; running {AppVersion.Current.ToString(3)}");
            return false;
        }

        if (pending.FailedAttempts >= MaxFailedAttempts)
        {
            TryDelete(staged);
            UpdateService.ClearPending();
            Log($"giving up on {pending.Version} after {pending.FailedAttempts} attempts: {pending.LastError}");
            return false;
        }

        try
        {
            // Re-verify: this file has been sitting on disk since it was downloaded, and it is
            // about to become the executable that runs every Sunday.
            if (pending.Sha256 is { Length: > 0 } expected)
            {
                var actual = UpdateService.ComputeSha256Async(staged, CancellationToken.None).GetAwaiter().GetResult();
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(staged);
                    UpdateService.ClearPending();
                    Log($"staged {pending.Version} failed its checksum; discarded");
                    return false;
                }
            }

            var script = WriteHelperScript(staged, running);

            // Counted as an attempt up front: this process is about to exit, so it will never
            // learn whether the helper succeeded. A swap that worked is recognised on the next
            // launch by the version check above, which clears the record. One that keeps
            // failing runs out of attempts instead of retrying forever.
            pending.FailedAttempts++;
            pending.LastError = "helper started; outcome unknown until next launch";
            UpdateService.WritePending(pending);

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            })?.Dispose();

            Log($"handed {AppVersion.Current.ToString(3)} -> {version.ToString(3)} to the helper");
            return true;
        }
        catch (Exception ex)
        {
            pending.FailedAttempts++;
            pending.LastError = ex.Message;
            UpdateService.WritePending(pending);
            Log($"attempt {pending.FailedAttempts} failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// PowerShell rather than a .cmd file: the waiting, retrying and error handling below are
    /// fiddly to get right in batch, and powershell.exe is present on every supported Windows.
    /// </summary>
    private static string WriteHelperScript(string staged, string target)
    {
        Directory.CreateDirectory(AppPaths.UpdatesDirectory);
        var path = Path.Combine(AppPaths.UpdatesDirectory, "apply-update.ps1");

        // $$"""…""" so PowerShell's own braces stay literal and {{…}} marks interpolation.
        var script = $$"""
            $ErrorActionPreference = 'SilentlyContinue'
            $log    = {{Quote(Path.Combine(AppPaths.UpdatesDirectory, "update.log"))}}
            $src    = {{Quote(staged)}}
            $dst    = {{Quote(target)}}
            $backup = {{Quote(target + BackupSuffix)}}
            $ownPid = {{Environment.ProcessId}}

            function Write-Step($message) {
                Add-Content -Path $log -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz') + ' | helper: ' + $message)
            }

            Write-Step 'waiting for SundayReady to exit'
            $deadline = (Get-Date).AddSeconds(60)
            while ((Get-Process -Id $ownPid -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
                Start-Sleep -Milliseconds 250
            }

            # Windows can hold the file briefly after the process goes; retry rather than fail.
            $moved = $false
            for ($i = 0; $i -lt 20 -and -not $moved; $i++) {
                try {
                    Move-Item -LiteralPath $dst -Destination $backup -Force -ErrorAction Stop
                    Move-Item -LiteralPath $src -Destination $dst -Force -ErrorAction Stop
                    $moved = $true
                } catch {
                    if (Test-Path -LiteralPath $backup) {
                        Move-Item -LiteralPath $backup -Destination $dst -Force -ErrorAction SilentlyContinue
                    }
                    Start-Sleep -Milliseconds 500
                }
            }

            if (-not $moved) {
                Write-Step 'could not replace the executable; leaving the old build in place'
                exit 1
            }

            Write-Step 'replaced the executable, restarting'
            Start-Process -FilePath $dst -WorkingDirectory (Split-Path -Parent $dst)
            """;

        File.WriteAllText(path, script);
        return path;
    }

    /// <summary>Removes the previous build left behind by the last successful swap.</summary>
    private static void CleanupBackup()
    {
        if (Environment.ProcessPath is { } running)
        {
            TryDelete(running + BackupSuffix);
        }
    }

    /// <summary>
    /// Updates happen unattended, before any window exists, on a PC nobody is watching.
    /// Without this there is no way to find out why a station is still on an old build.
    /// </summary>
    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.UpdatesDirectory);
            File.AppendAllText(
                Path.Combine(AppPaths.UpdatesDirectory, "update.log"),
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} | {message}{Environment.NewLine}",
                System.Text.Encoding.UTF8);
        }
        catch (Exception)
        {
            // If we cannot even log, there is nothing further to try.
        }
    }

    private static string Quote(string value) => $"'{value.Replace("'", "''")}'";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Still locked, or not ours to delete. It gets another chance next launch.
        }
    }
}
