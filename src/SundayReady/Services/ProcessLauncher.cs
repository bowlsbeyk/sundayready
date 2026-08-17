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
            var target = Environment.ExpandEnvironmentVariables(action.Run);
            var args = string.IsNullOrWhiteSpace(action.Args)
                ? null
                : Environment.ExpandEnvironmentVariables(action.Args);

            Process.Start(OperatingSystem.IsMacOS() ? MacStartInfo(target, args) : WindowsStartInfo(target, args))
                ?.Dispose();

            return LaunchResult.Ok();
        }
        catch (Exception ex)
        {
            return LaunchResult.Failed(ex.Message);
        }
    }

    private static ProcessStartInfo WindowsStartInfo(string target, string? args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = target,
            // Shell execution is what makes URLs and .bat/.ps1 files work the same way an
            // operator double-clicking them would.
            UseShellExecute = true,
        };

        if (args is not null)
        {
            startInfo.Arguments = args;
        }

        return startInfo;
    }

    /// <summary>
    /// macOS needs <c>open</c> spelled out rather than <c>UseShellExecute</c>, which on Unix
    /// silently drops <see cref="ActionSpec.Args"/> — a launch button that quietly ignores half
    /// its configuration is worse than one that fails.
    /// <para>
    /// An <c>.app</c> is launched with <c>open -a</c> so it joins the GUI session properly;
    /// a URL goes to <c>open</c> to reach the default browser; anything else is a plain
    /// executable or script and is run directly, which is the only form that takes arguments
    /// reliably.
    /// </para>
    /// </summary>
    private static ProcessStartInfo MacStartInfo(string target, string? args)
    {
        var startInfo = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true };

        var isUrl = Uri.TryCreate(target, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" or "mailto";

        if (target.EndsWith(".app", StringComparison.OrdinalIgnoreCase) || Directory.Exists(target))
        {
            startInfo.FileName = "/usr/bin/open";
            startInfo.ArgumentList.Add("-a");
            startInfo.ArgumentList.Add(target);

            if (args is not null)
            {
                // Everything after --args is handed to the app rather than to open itself.
                startInfo.ArgumentList.Add("--args");
                foreach (var argument in SplitArguments(args))
                {
                    startInfo.ArgumentList.Add(argument);
                }
            }
        }
        else if (isUrl)
        {
            startInfo.FileName = "/usr/bin/open";
            startInfo.ArgumentList.Add(target);
        }
        else
        {
            startInfo.FileName = target;
            foreach (var argument in SplitArguments(args))
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        return startInfo;
    }

    /// <summary>
    /// Splits an <c>args</c> string on spaces, keeping quoted runs together. <c>open --args</c>
    /// wants them separately, and <c>station.json</c> holds them as one line because that is how
    /// someone reads a command off a shortcut's properties.
    /// </summary>
    private static IEnumerable<string> SplitArguments(string? args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            yield break;
        }

        var current = new System.Text.StringBuilder();
        var quote = '\0';

        foreach (var c in args)
        {
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }
}
