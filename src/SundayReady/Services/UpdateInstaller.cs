using System.Diagnostics;

namespace SundayReady.Services;

/// <summary>
/// Swaps a staged update in and restarts into it.
/// <para>
/// The swap is always done by a short-lived helper script rather than by this process, because a
/// self-contained build resolves assemblies lazily out of its own file. Move or replace it while
/// it is running and the next assembly it needs is simply gone — which is exactly what happens
/// when you try to relaunch after replacing yourself. So this process writes the helper and
/// starts it while it is still intact, then exits; the helper waits for it to go, moves the new
/// build into place, and starts it.
/// </para>
/// <para>
/// Two ways in. <see cref="TryApply"/> runs at launch, before any UI exists, and is how an
/// unattended station picks up what it staged overnight. <see cref="TryRestartInto"/> is the
/// operator asking for it now from the settings screen. Both end in the same handoff.
/// </para>
/// </summary>
public static class UpdateInstaller
{
    private const int MaxFailedAttempts = 3;

    private const string BackupSuffix = ".old";

    /// <summary>
    /// Returns true when a swap has been handed off to the helper — the caller must exit
    /// immediately without initialising Avalonia, so the build is free to be replaced.
    /// </summary>
    public static bool TryApply()
    {
        CleanupBackup();

        var pending = UpdateService.ReadPending();
        if (pending is null)
        {
            return false;
        }

        if (!Eligible(pending, out var version))
        {
            return false;
        }

        if (pending.FailedAttempts >= MaxFailedAttempts)
        {
            TryDelete(pending.File);
            UpdateService.ClearPending();
            Log($"giving up on {pending.Version} after {pending.FailedAttempts} attempts: {pending.LastError}");
            return false;
        }

        return HandOff(pending, version);
    }

    /// <summary>
    /// Installs a staged update straight away. Returns true once the helper has been started, at
    /// which point the caller must shut the app down — the helper is already waiting for it.
    /// <para>
    /// Unlike <see cref="TryApply"/> this ignores the failed-attempt count: an operator standing
    /// in front of the machine pressing the button is entitled to another try, and will see the
    /// reason in <paramref name="error"/> if it still cannot be done.
    /// </para>
    /// </summary>
    public static bool TryRestartInto(PendingUpdate pending, out string? error)
    {
        error = null;

        if (!AppPlatform.CanSelfUpdate)
        {
            error = "This build cannot replace itself in place. Download the new one from the releases page.";
            return false;
        }

        if (!Eligible(pending, out var version))
        {
            error = "The staged update is no longer valid. Check for updates again.";
            return false;
        }

        if (!HandOff(pending, version))
        {
            error = UpdateService.ReadPending()?.LastError ?? "The update helper could not be started.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Confirms the staged file is still there, still verifies, and is still newer than what is
    /// running. Clears the record when it is not, so a stale download does not sit forever.
    /// </summary>
    private static bool Eligible(PendingUpdate pending, out ReleaseVersion version)
    {
        version = new ReleaseVersion(new Version(0, 0, 0), ReleaseChannel.Production, 0);

        if (AppPlatform.InstallRoot is null || !File.Exists(pending.File))
        {
            UpdateService.ClearPending();
            return false;
        }

        // Already on this version or newer. Either the last swap worked and this is the
        // clean-up pass, or the staged build is simply stale.
        if (ReleaseVersion.Parse(pending.Version) is not { } parsed || parsed <= AppVersion.Current)
        {
            TryDelete(pending.File);
            UpdateService.ClearPending();
            Log($"cleared staged {pending.Version}; running {AppVersion.Current.Text}");
            return false;
        }

        // Re-verify: this file has been sitting on disk since it was downloaded, and it is
        // about to become the build that runs every Sunday.
        if (pending.Sha256 is { Length: > 0 } expected)
        {
            string actual;
            try
            {
                actual = UpdateService.ComputeSha256Async(pending.File, CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log($"could not read staged {pending.Version}: {ex.Message}");
                return false;
            }

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(pending.File);
                UpdateService.ClearPending();
                Log($"staged {pending.Version} failed its checksum; discarded");
                return false;
            }
        }

        version = parsed;
        return true;
    }

    /// <summary>Writes the helper, starts it, and records the attempt.</summary>
    private static bool HandOff(PendingUpdate pending, ReleaseVersion version)
    {
        var target = AppPlatform.InstallRoot;
        if (target is null)
        {
            return false;
        }

        try
        {
            var (executable, arguments) = AppPlatform.IsWindows
                ? WriteWindowsHelper(pending.File, target)
                : WriteUnixHelper(pending.File, target);

            // Counted as an attempt up front: this process is about to exit, so it will never
            // learn whether the helper succeeded. A swap that worked is recognised on the next
            // launch by the version check in Eligible, which clears the record. One that keeps
            // failing runs out of attempts instead of retrying forever.
            pending.FailedAttempts++;
            pending.LastError = "helper started; outcome unknown until next launch";
            UpdateService.WritePending(pending);

            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
            })?.Dispose();

            Log($"handed {AppVersion.Current.Text} -> {version.Text} to the helper ({AppPlatform.Rid})");
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
    private static (string Executable, string Arguments) WriteWindowsHelper(string staged, string target)
    {
        Directory.CreateDirectory(AppPaths.UpdatesDirectory);
        var path = Path.Combine(AppPaths.UpdatesDirectory, "apply-update.ps1");

        // $$"""…""" so PowerShell's own braces stay literal and {{…}} marks interpolation.
        var script = $$"""
            $ErrorActionPreference = 'SilentlyContinue'
            $log    = {{PowerShellQuote(Path.Combine(AppPaths.UpdatesDirectory, "update.log"))}}
            $src    = {{PowerShellQuote(staged)}}
            $dst    = {{PowerShellQuote(target)}}
            $backup = {{PowerShellQuote(target + BackupSuffix)}}
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

        return ("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{path}\"");
    }

    /// <summary>
    /// The macOS side. The staged file is a zip of <c>SundayReady.app</c>, and the bundle is
    /// replaced as a unit — see <see cref="InstallLayout.AppBundle"/> for why it cannot be a
    /// single file. <c>ditto</c> rather than <c>unzip</c> because it is the one tool that
    /// preserves the symlinks and resource forks inside a bundle.
    /// </summary>
    private static (string Executable, string Arguments) WriteUnixHelper(string staged, string target)
    {
        Directory.CreateDirectory(AppPaths.UpdatesDirectory);
        var path = Path.Combine(AppPaths.UpdatesDirectory, "apply-update.sh");

        var script = $$"""
            #!/bin/sh
            # Written by SundayReady. Replaces the running .app with a staged download.
            log={{ShellQuote(Path.Combine(AppPaths.UpdatesDirectory, "update.log"))}}
            src={{ShellQuote(staged)}}
            dst={{ShellQuote(target)}}
            backup={{ShellQuote(target + BackupSuffix)}}
            own_pid={{Environment.ProcessId}}
            work={{ShellQuote(Path.Combine(AppPaths.UpdatesDirectory, "unpack"))}}

            step() {
                printf '%s | helper: %s\n' "$(date '+%Y-%m-%d %H:%M:%S %z')" "$1" >> "$log" 2>/dev/null
            }

            step 'waiting for SundayReady to exit'
            i=0
            while kill -0 "$own_pid" 2>/dev/null && [ "$i" -lt 240 ]; do
                sleep 0.25
                i=$((i + 1))
            done

            rm -rf "$work"
            mkdir -p "$work" || { step 'could not create the unpack directory'; exit 1; }

            if ! /usr/bin/ditto -x -k "$src" "$work" 2>>"$log"; then
                step 'could not expand the staged archive'
                rm -rf "$work"
                exit 1
            fi

            # Whatever the archive called its top-level folder, take the first .app in it.
            new=$(find "$work" -maxdepth 2 -name '*.app' -type d -print 2>/dev/null | head -n 1)
            if [ -z "$new" ]; then
                step 'the staged archive contained no .app bundle'
                rm -rf "$work"
                exit 1
            fi

            # Downloads made by the app are not quarantined, but a bundle that reached the
            # updates folder some other way would be, and Gatekeeper would refuse to open it.
            /usr/bin/xattr -dr com.apple.quarantine "$new" 2>/dev/null

            rm -rf "$backup"
            if ! mv "$dst" "$backup" 2>>"$log"; then
                step 'could not move the old bundle aside; leaving it in place'
                rm -rf "$work"
                exit 1
            fi

            if ! mv "$new" "$dst" 2>>"$log"; then
                step 'could not move the new bundle into place; putting the old one back'
                mv "$backup" "$dst" 2>>"$log"
                rm -rf "$work"
                exit 1
            fi

            rm -rf "$work" "$backup" "$src"
            step 'replaced the bundle, restarting'
            /usr/bin/open -n "$dst"
            """;

        File.WriteAllText(path, script.ReplaceLineEndings("\n"));

        // 0755. The file was just created by this process, so there is nothing to race with.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        return ("/bin/sh", $"\"{path}\"");
    }

    /// <summary>Removes the previous build left behind by the last successful swap.</summary>
    private static void CleanupBackup()
    {
        if (AppPlatform.InstallRoot is not { } running)
        {
            return;
        }

        var backup = running + BackupSuffix;
        try
        {
            if (Directory.Exists(backup))
            {
                Directory.Delete(backup, recursive: true);
            }
            else
            {
                TryDelete(backup);
            }
        }
        catch (Exception)
        {
            // Still locked, or not ours to delete. It gets another chance next launch.
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

    private static string PowerShellQuote(string value) => $"'{value.Replace("'", "''")}'";

    private static string ShellQuote(string value) => $"'{value.Replace("'", "'\\''")}'";

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
