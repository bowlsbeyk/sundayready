using System.Runtime.InteropServices;

namespace SundayReady.Services;

/// <summary>How the running build is laid out on disk, which decides how it can be replaced.</summary>
public enum InstallLayout
{
    /// <summary>Not something we know how to update — a loose build, or an unrecognised OS.</summary>
    Unknown,

    /// <summary>One self-contained executable. Windows: everything is inside the .exe.</summary>
    SingleFile,

    /// <summary>
    /// A macOS <c>.app</c> directory. A single-file publish for osx still leaves the Skia,
    /// HarfBuzz and Avalonia dylibs next to the binary, so the unit that gets replaced is the
    /// whole bundle — which is also what keeps <c>Info.plist</c> and the icon in step with it.
    /// </summary>
    AppBundle,
}

/// <summary>
/// The per-OS facts the rest of the app needs: which release asset belongs to this machine,
/// what has to be replaced to update it, and which platform features actually exist here.
/// <para>
/// Everything Windows-only is named here rather than tested for with <c>IsWindows()</c> at each
/// call site, so adding a third OS is a matter of extending this file.
/// </para>
/// </summary>
public static class AppPlatform
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    public static bool IsMacOS => OperatingSystem.IsMacOS();

    /// <summary>The RID this build was published for, used to pick a release asset.</summary>
    public static string Rid
    {
        get
        {
            var arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            };

            if (IsWindows)
            {
                return $"win-{arch}";
            }

            return IsMacOS ? $"osx-{arch}" : $"linux-{arch}";
        }
    }

    /// <summary>Friendly name for the settings screen: <c>macOS (Apple Silicon)</c>.</summary>
    public static string Description => Rid switch
    {
        "win-x64" => "Windows (64-bit)",
        "win-arm64" => "Windows (ARM)",
        "osx-arm64" => "macOS (Apple Silicon)",
        "osx-x64" => "macOS (Intel)",
        _ => Rid,
    };

    /// <summary>
    /// The release asset the updater downloads for this machine. Windows takes the bare exe so
    /// an update never touches a station's checklists; macOS takes the zipped bundle, because
    /// the bundle is the unit (see <see cref="InstallLayout.AppBundle"/>).
    /// </summary>
    public static string UpdateAssetName => IsMacOS
        ? $"SundayReady-{Rid}.zip"
        : $"SundayReady-{Rid}.exe";

    public static InstallLayout Layout
    {
        get
        {
            if (Environment.ProcessPath is not { Length: > 0 } running)
            {
                return InstallLayout.Unknown;
            }

            if (IsWindows)
            {
                // Launched as "dotnet SundayReady.dll", the process is the shared runtime — and
                // an updater that took that at face value would replace dotnet.exe with a copy
                // of this app. Only a build that is its own executable can replace itself.
                var isRuntimeHost = string.Equals(
                    Path.GetFileNameWithoutExtension(running),
                    "dotnet",
                    StringComparison.OrdinalIgnoreCase);

                return !isRuntimeHost && running.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? InstallLayout.SingleFile
                    : InstallLayout.Unknown;
            }

            return IsMacOS && BundleRoot(running) is not null
                ? InstallLayout.AppBundle
                : InstallLayout.Unknown;
        }
    }

    /// <summary>
    /// What an update replaces: the .exe on Windows, the <c>.app</c> directory on macOS.
    /// Null when this build is not laid out in a way we can safely replace — running from
    /// <c>bin/Debug</c>, say, where the updater must stay out of the way.
    /// </summary>
    public static string? InstallRoot
    {
        get
        {
            if (Environment.ProcessPath is not { Length: > 0 } running)
            {
                return null;
            }

            return Layout switch
            {
                InstallLayout.SingleFile => running,
                InstallLayout.AppBundle => BundleRoot(running),
                _ => null,
            };
        }
    }

    /// <summary>True when this build can replace itself in place.</summary>
    public static bool CanSelfUpdate => InstallRoot is not null;

    /// <summary>
    /// Whether stored credentials survive a restart. Windows has DPAPI and macOS has the
    /// Keychain; anywhere else <see cref="SecretStore"/> declines to write, and the settings
    /// screen says so rather than pretending a pasted API key was saved.
    /// </summary>
    public static bool SupportsSecretStorage => IsWindows || IsMacOS;

    /// <summary>Whether the app can register itself to start when the operator logs in.</summary>
    public static bool SupportsStartAtLogon => IsWindows || IsMacOS;

    /// <summary>
    /// Walks up from the executable to the <c>.app</c> it lives in. A bundle is always
    /// <c>Foo.app/Contents/MacOS/binary</c>, so this checks that shape rather than trusting
    /// any ancestor that happens to end in <c>.app</c>.
    /// </summary>
    private static string? BundleRoot(string executable)
    {
        var macOs = Path.GetDirectoryName(executable);
        if (macOs is null || !string.Equals(Path.GetFileName(macOs), "MacOS", StringComparison.Ordinal))
        {
            return null;
        }

        var contents = Path.GetDirectoryName(macOs);
        if (contents is null || !string.Equals(Path.GetFileName(contents), "Contents", StringComparison.Ordinal))
        {
            return null;
        }

        var bundle = Path.GetDirectoryName(contents);
        return bundle is not null && bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
            ? bundle
            : null;
    }
}
