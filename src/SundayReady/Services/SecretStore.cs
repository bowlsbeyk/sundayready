using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace SundayReady.Services;

/// <summary>
/// Holds credentials the app needs, encrypted at rest by whatever the operating system provides:
/// DPAPI on Windows, the login Keychain on macOS.
/// <para>
/// Deliberately not in <c>station.json</c>. That file is meant to be readable, copied between
/// stations, and mailed around; a Facebook Page token is none of those things. Keeping it
/// separate means copying a station's config never carries its credentials with it.
/// </para>
/// <para>
/// Either way the secret is tied to this user on this machine, so a copy of it taken elsewhere is
/// useless. That is the point — this is not a secret vault, it just means a token is not sitting
/// in plain text on a booth PC that volunteers use.
/// </para>
/// </summary>
public static class SecretStore
{
    /// <summary>Keychain service name. Prefixed so the entries are recognisable in Keychain Access.</summary>
    private static string ServiceFor(string name) => $"SundayReady.{name}";

    private static string PathFor(string name) =>
        Path.Combine(AppPaths.DataDirectory, $"{name}.secret");

    public static bool Has(string name) => Read(name) is not null;

    public static string? Read(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            return ReadDpapi(name);
        }

        return OperatingSystem.IsMacOS() ? ReadKeychain(name) : null;
    }

    public static void Write(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Delete(name);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            WriteDpapi(name, value.Trim());
        }
        else if (OperatingSystem.IsMacOS())
        {
            WriteKeychain(name, value.Trim());
        }
    }

    public static void Delete(string name)
    {
        if (OperatingSystem.IsMacOS())
        {
            // -a and -s together identify the one entry this app wrote.
            RunSecurity(new[] { "delete-generic-password", "-a", Account, "-s", ServiceFor(name) }, out _);
            return;
        }

        try
        {
            var path = PathFor(name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Nothing useful to do; the value is overwritten next time one is saved.
        }
    }

    /// <summary>A masked form for the UI, so a saved token can be shown as present without being shown.</summary>
    public static string Describe(string name)
    {
        if (!AppPlatform.SupportsSecretStorage)
        {
            return "Cannot be saved on this platform — there is no credential store to put it in.";
        }

        var value = Read(name);
        if (value is null)
        {
            return "Not set.";
        }

        var tail = value.Length >= 4 ? $" ending {value[^4..]}" : string.Empty;
        var where = OperatingSystem.IsMacOS()
            ? "Held in the login Keychain."
            : "Encrypted for this Windows user.";

        return $"Saved · {value.Length} characters{tail}. {where}";
    }

    private static string? ReadDpapi(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path) || !OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception)
        {
            // Written by a different Windows user, or the profile was rebuilt. Either way the
            // stored value is unrecoverable and the operator has to paste it again.
            return null;
        }
    }

    private static void WriteDpapi(string name, string value)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(AppPaths.DataDirectory);
        var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(PathFor(name), cipher);
    }

    private static string Account => Environment.UserName;

    private static string? ReadKeychain(string name)
    {
        // -w prints just the password. A missing entry exits non-zero, which is not an error.
        var ok = RunSecurity(
            new[] { "find-generic-password", "-a", Account, "-s", ServiceFor(name), "-w" },
            out var output);

        return ok && output.Length > 0 ? output : null;
    }

    private static void WriteKeychain(string name, string value)
    {
        // -U updates the entry if it already exists instead of failing as a duplicate.
        //
        // The value goes through argv, which means it is briefly visible to `ps` on this
        // machine. That is the documented cost of the `security` tool having no way to take a
        // password on stdin non-interactively, and it buys real Keychain encryption at rest —
        // which beats the alternative of a plaintext file by a wide margin.
        RunSecurity(
            new[] { "add-generic-password", "-a", Account, "-s", ServiceFor(name), "-U", "-w", value },
            out _);
    }

    /// <summary>
    /// Runs <c>/usr/bin/security</c> and returns whether it succeeded, with stdout trimmed.
    /// Arguments are passed as a list rather than a command line, so nothing in a pasted token
    /// can be read as shell syntax.
    /// </summary>
    private static bool RunSecurity(IReadOnlyList<string> arguments, out string output)
    {
        output = string.Empty;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/security",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            output = process.StandardOutput.ReadToEnd().Trim();
            process.StandardError.ReadToEnd();

            // Generous: the Keychain can prompt for permission the first time, and the operator
            // has to be able to reach the dialog.
            if (!process.WaitForExit(30_000))
            {
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
