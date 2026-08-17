using System.Diagnostics;
using System.Text;

namespace SundayReady.Services;

public sealed record TaskResult(bool Succeeded, string Message);

/// <summary>
/// Registers SundayReady to start when the operator logs in: Task Scheduler on Windows, a
/// LaunchAgent on macOS.
/// <para>
/// Task Scheduler rather than <c>shell:startup</c> because the startup folder races the
/// services the verifiers check for — vMix's web controller is not listening the instant the
/// desktop appears. The task waits 30 seconds and restarts on failure. The LaunchAgent waits the
/// same 30 seconds, for the same reason.
/// </para>
/// </summary>
public static class LogonTask
{
    public const string TaskName = "SundayReady";

    /// <summary>Reverse-DNS label, which is what launchd wants and what shows up in its logs.</summary>
    public const string LaunchAgentLabel = "org.trinitybaptist.sundayready";

    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(30);

    public static bool IsRegistered()
    {
        if (OperatingSystem.IsMacOS())
        {
            return File.Exists(LaunchAgentPath);
        }

        var (exit, _, _) = Run($"/query /tn \"{TaskName}\"");
        return exit == 0;
    }

    /// <param name="exePath">
    /// Defaults to the running build. Takes an override so the registration can be exercised
    /// without the test harness registering <em>itself</em>.
    /// </param>
    public static TaskResult Register(string? exePath = null)
    {
        if (OperatingSystem.IsMacOS())
        {
            return RegisterLaunchAgent(exePath);
        }

        var exe = exePath ?? Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return new TaskResult(false, "Only a published build can register itself — this looks like a dev run.");
        }

        var xmlPath = Path.Combine(Path.GetTempPath(), $"sundayready-task-{Environment.ProcessId}.xml");

        try
        {
            // schtasks requires the definition file to be UTF-16.
            File.WriteAllText(xmlPath, BuildTaskXml(exe), Encoding.Unicode);

            var (exit, output, error) = Run($"/create /tn \"{TaskName}\" /xml \"{xmlPath}\" /f");
            if (exit != 0)
            {
                var detail = string.IsNullOrWhiteSpace(error) ? output : error;
                return new TaskResult(false, $"Task Scheduler refused it: {detail.Trim()}");
            }

            return new TaskResult(true,
                $"Registered. SundayReady will start {Delay.TotalSeconds:0} seconds after logon, from {exe}.");
        }
        catch (Exception ex)
        {
            return new TaskResult(false, ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(xmlPath))
                {
                    File.Delete(xmlPath);
                }
            }
            catch (Exception)
            {
                // A stray temp file is not worth reporting.
            }
        }
    }

    public static TaskResult Unregister()
    {
        if (OperatingSystem.IsMacOS())
        {
            return UnregisterLaunchAgent();
        }

        var (exit, output, error) = Run($"/delete /tn \"{TaskName}\" /f");

        return exit == 0
            ? new TaskResult(true, "Removed. SundayReady will no longer start at logon.")
            : new TaskResult(false, (string.IsNullOrWhiteSpace(error) ? output : error).Trim());
    }

    /// <summary>
    /// Hand-built XML rather than schtasks' own flags, because the restart-on-failure and
    /// "start when available" settings the booth needs cannot be expressed on the command line.
    /// </summary>
    public static string BuildTaskXml(string exe)
    {
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var workingDirectory = Path.GetDirectoryName(exe) ?? string.Empty;

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Opens the Sunday preflight checklist for this A/V station.</Description>
                <URI>\{TaskName}</URI>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <Delay>PT{Delay.TotalSeconds:0}S</Delay>
                  <UserId>{Escape(user)}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
                <RestartOnFailure>
                  <Interval>PT1M</Interval>
                  <Count>3</Count>
                </RestartOnFailure>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{Escape(exe)}</Command>
                  <WorkingDirectory>{Escape(workingDirectory)}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static string LaunchAgentPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", $"{LaunchAgentLabel}.plist");

    /// <param name="bundlePath">
    /// Defaults to the running <c>.app</c>. Takes an override for the same reason the Windows
    /// side does.
    /// </param>
    private static TaskResult RegisterLaunchAgent(string? bundlePath)
    {
        var bundle = bundlePath ?? AppPlatform.InstallRoot;
        if (string.IsNullOrEmpty(bundle) || !bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            return new TaskResult(false,
                "Only an installed SundayReady.app can register itself — this looks like a dev run.");
        }

        try
        {
            var path = LaunchAgentPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, BuildLaunchAgentPlist(bundle));

            // bootstrap is the current verb; load is the one that works on older macOS. Try the
            // modern one and fall back, because a station could be on anything from Monterey up.
            if (!LaunchCtl(new[] { "bootstrap", $"gui/{Uid}", path }).Succeeded)
            {
                var legacy = LaunchCtl(new[] { "load", "-w", path });
                if (!legacy.Succeeded)
                {
                    // The plist is on disk either way, so it takes effect at the next login even
                    // if launchd would not take it now. Say so rather than claiming failure.
                    return new TaskResult(true,
                        $"Registered for the next login. launchd would not load it now: {legacy.Message}");
                }
            }

            return new TaskResult(true,
                $"Registered. SundayReady will open {Delay.TotalSeconds:0} seconds after login, from {bundle}.");
        }
        catch (Exception ex)
        {
            return new TaskResult(false, ex.Message);
        }
    }

    private static TaskResult UnregisterLaunchAgent()
    {
        var path = LaunchAgentPath;

        if (!File.Exists(path))
        {
            return new TaskResult(true, "Was not registered.");
        }

        if (!LaunchCtl(new[] { "bootout", $"gui/{Uid}/{LaunchAgentLabel}" }).Succeeded)
        {
            LaunchCtl(new[] { "unload", "-w", path });
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            return new TaskResult(false, $"Could not remove {path}: {ex.Message}");
        }

        return new TaskResult(true, "Removed. SundayReady will no longer open at login.");
    }

    /// <summary>
    /// launchd has no delay key, so the wait is a shell sleep. <c>open</c> rather than the binary
    /// directly, so the app gets a Dock icon and a normal GUI session instead of running as a
    /// headless child of launchd.
    /// </summary>
    public static string BuildLaunchAgentPlist(string bundle)
    {
        var command = $"sleep {Delay.TotalSeconds:0}; exec /usr/bin/open -a {ShellQuote(bundle)}";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key>
              <string>{LaunchAgentLabel}</string>
              <key>ProgramArguments</key>
              <array>
                <string>/bin/sh</string>
                <string>-c</string>
                <string>{Escape(command)}</string>
              </array>
              <key>RunAtLoad</key>
              <true/>
              <key>ProcessType</key>
              <string>Interactive</string>
            </dict>
            </plist>
            """;
    }

    /// <summary>
    /// The numeric user id, which is the domain launchctl wants. <c>id -u</c> rather than
    /// anything in .NET, because there is no managed API for it.
    /// </summary>
    private static string Uid => RunCapture("/usr/bin/id", new[] { "-u" }) ?? "501";

    private static string ShellQuote(string value) => $"'{value.Replace("'", "'\\''")}'";

    private static TaskResult LaunchCtl(IReadOnlyList<string> arguments)
    {
        var (exit, output, error) = RunTool("/bin/launchctl", arguments);
        var message = string.IsNullOrWhiteSpace(error) ? output : error;
        return new TaskResult(exit == 0, message.Trim());
    }

    private static string? RunCapture(string fileName, IReadOnlyList<string> arguments)
    {
        var (exit, output, _) = RunTool(fileName, arguments);
        var trimmed = output.Trim();
        return exit == 0 && trimmed.Length > 0 ? trimmed : null;
    }

    /// <summary>
    /// Arguments as a list rather than a command line, so nothing in a path with a space or a
    /// quote in it can be re-read as syntax.
    /// </summary>
    private static (int Exit, string Output, string Error) RunTool(
        string fileName,
        IReadOnlyList<string> arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return (-1, string.Empty, $"Could not start {fileName}.");
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(15000);

            return (process.ExitCode, output, error);
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }

    private static string Escape(string value) => value
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static (int Exit, string Output, string Error) Run(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return (-1, string.Empty, "Could not start schtasks.exe.");
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(15000);

            return (process.ExitCode, output, error);
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }
}
