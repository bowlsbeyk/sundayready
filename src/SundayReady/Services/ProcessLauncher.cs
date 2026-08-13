using System.Diagnostics;
using SundayReady.Models;

namespace SundayReady.Services;

public sealed record LaunchResult(bool Succeeded, string? Error)
{
    public static LaunchResult Ok() => new(true, null);

    public static LaunchResult Failed(string error) => new(false, error);
}

/// <summary>
/// Runs whatever an <c>action</c> item points at. A launch failure is reported back to the
/// operator on the item; it never takes the app down mid-Sunday.
/// </summary>
public sealed class ProcessLauncher
{
    /// <summary>Launches the action and everything in its <c>also</c> list, stopping at the first failure.</summary>
    public LaunchResult Launch(ActionSpec action)
    {
        var result = LaunchOne(action);
        if (!result.Succeeded)
        {
            return result;
        }

        foreach (var additional in action.Also)
        {
            result = LaunchOne(additional);
            if (!result.Succeeded)
            {
                return result;
            }
        }

        return LaunchResult.Ok();
    }

    private static LaunchResult LaunchOne(ActionSpec action)
    {
        if (string.IsNullOrWhiteSpace(action.Run))
        {
            return LaunchResult.Failed("This item has no command configured.");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.ExpandEnvironmentVariables(action.Run),
                // Shell execution is what makes URLs and .bat/.ps1 files work the same way
                // an operator double-clicking them would.
                UseShellExecute = true,
            };

            if (!string.IsNullOrWhiteSpace(action.Args))
            {
                startInfo.Arguments = Environment.ExpandEnvironmentVariables(action.Args);
            }

            Process.Start(startInfo)?.Dispose();
            return LaunchResult.Ok();
        }
        catch (Exception ex)
        {
            return LaunchResult.Failed(ex.Message);
        }
    }
}
