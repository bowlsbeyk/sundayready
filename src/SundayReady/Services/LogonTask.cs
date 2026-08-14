using System.Diagnostics;
using System.Text;

namespace SundayReady.Services;

public sealed record TaskResult(bool Succeeded, string Message);

/// <summary>
/// Registers SundayReady to start at logon via Task Scheduler.
/// <para>
/// Task Scheduler rather than <c>shell:startup</c> because the startup folder races the
/// services the verifiers check for — vMix's web controller is not listening the instant the
/// desktop appears. The task waits 30 seconds and restarts on failure.
/// </para>
/// </summary>
public static class LogonTask
{
    public const string TaskName = "SundayReady";

    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(30);

    public static bool IsRegistered()
    {
        var (exit, _, _) = Run($"/query /tn \"{TaskName}\"");
        return exit == 0;
    }

    public static TaskResult Register()
    {
        var exe = Environment.ProcessPath;
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
