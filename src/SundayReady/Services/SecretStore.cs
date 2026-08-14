using System.Security.Cryptography;
using System.Text;

namespace SundayReady.Services;

/// <summary>
/// Holds credentials the app needs, encrypted at rest with Windows DPAPI under the logged-in
/// user's key.
/// <para>
/// Deliberately not in <c>station.json</c>. That file is meant to be readable, copied between
/// stations, and mailed around; a Facebook Page token is none of those things. Keeping it
/// separate means copying a station's config never carries its credentials with it.
/// </para>
/// <para>
/// DPAPI ties the ciphertext to this Windows user on this machine, so the file is useless if
/// it is copied elsewhere. That is the point — it is not a secret vault, it just means a
/// token is not sitting in plain text on a booth PC that volunteers use.
/// </para>
/// </summary>
public static class SecretStore
{
    private static string PathFor(string name) =>
        Path.Combine(AppPaths.DataDirectory, $"{name}.secret");

    public static bool Has(string name) => File.Exists(PathFor(name));

    public static string? Read(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
        {
            return null;
        }

        // DPAPI is Windows-only. The app is too, but the target framework does not say so,
        // so the guard is what tells the analyser — and what would stop a crash if this ever
        // ran anywhere else.
        if (!OperatingSystem.IsWindows())
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

    public static void Write(string name, string? value)
    {
        var path = PathFor(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            Delete(name);
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(AppPaths.DataDirectory);
        var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, cipher);
    }

    public static void Delete(string name)
    {
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
        var value = Read(name);
        return value is null
            ? "Not set."
            : $"Saved · {value.Length} characters ending {value[^4..]}. Encrypted for this Windows user.";
    }
}
