using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SundayReady.Services;

/// <summary>A release newer than the running build, and the asset to fetch.</summary>
public sealed record AvailableUpdate(
    ReleaseVersion Version,
    string Tag,
    string DownloadUrl,
    long Size,
    string? Sha256);

/// <summary>An update already downloaded and waiting for the next launch.</summary>
public sealed class PendingUpdate
{
    public string Version { get; set; } = string.Empty;

    public string File { get; set; } = string.Empty;

    public string? Sha256 { get; set; }

    /// <summary>Bumped each time the swap fails, so a permanently unwritable install gives up.</summary>
    public int FailedAttempts { get; set; }

    public string? LastError { get; set; }
}

/// <summary>
/// Checks GitHub Releases for a newer build and stages it on disk. Applying it is
/// <see cref="UpdateInstaller"/>'s job — either at the next launch, or immediately when an
/// operator asks for it from the settings screen. Nothing is ever swapped unasked mid-service.
/// <para>
/// The repository is public, so no token is involved and none of this touches credentials.
/// </para>
/// </summary>
public sealed class UpdateService : IDisposable
{
    public const string DefaultRepository = "bowlsbeyk/sundayready";

    /// <summary>
    /// How many releases back to look. A station left off for a season can be several releases
    /// behind, and on a narrow channel most of the recent tags will not be ones it accepts.
    /// </summary>
    private const int ReleasesToScan = 40;

    private readonly HttpClient _client;
    private readonly string _repository;

    public UpdateService(string? repository = null)
    {
        _repository = string.IsNullOrWhiteSpace(repository) ? DefaultRepository : repository;
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // The GitHub API rejects requests without a User-Agent.
        _client.DefaultRequestHeaders.Add("User-Agent", $"SundayReady/{AppVersion.Display}");
        _client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
    }

    public string Repository => _repository;

    public string ReleasesUrl => $"https://github.com/{_repository}/releases";

    /// <summary>
    /// Returns the newest release this station is willing to run, if it is newer than the
    /// running build. Network failure is not an error worth surfacing loudly — it throws, and
    /// the caller records it as a status line rather than bothering the operator.
    /// <para>
    /// Two endpoints, because neither one is enough on its own. <c>/releases/latest</c> is exact,
    /// cheap and always current, but it never returns a prerelease — a station on beta asking only
    /// that question would sit on production forever. The release list does return prereleases,
    /// but it is a cached collection endpoint that has been observed answering <c>200 []</c> for a
    /// repository whose releases are plainly there. So: ask the list when the channel needs it,
    /// and always fall back to <c>latest</c>, which means the worst case for a booth PC is that it
    /// finds the finished release rather than nothing at all.
    /// </para>
    /// </summary>
    public async Task<AvailableUpdate?> CheckAsync(ReleaseChannel channel, CancellationToken cancellationToken)
    {
        AvailableUpdate? best = null;

        if (channel != ReleaseChannel.Production)
        {
            var url = $"https://api.github.com/repos/{_repository}/releases?per_page={ReleasesToScan}";
            var releases = await _client
                .GetFromJsonAsync<List<GitHubRelease>>(url, cancellationToken)
                .ConfigureAwait(true);

            foreach (var release in releases ?? new List<GitHubRelease>())
            {
                best = Better(best, release, channel);
            }
        }

        // Always: a production release supersedes its own prereleases, so this can only ever
        // improve on what the list found — and it is the whole answer for a production station.
        var latest = await _client
            .GetFromJsonAsync<GitHubRelease>(
                $"https://api.github.com/repos/{_repository}/releases/latest",
                cancellationToken)
            .ConfigureAwait(true);

        if (latest is not null)
        {
            best = Better(best, latest, channel);
        }

        return best;
    }

    /// <summary>
    /// Returns <paramref name="release"/> as the new best candidate if this station may run it and
    /// it beats what we have, otherwise the incumbent.
    /// </summary>
    private static AvailableUpdate? Better(AvailableUpdate? best, GitHubRelease release, ReleaseChannel channel)
    {
        if (release.Draft)
        {
            return best;
        }

        // The tag is the authority on which channel a release belongs to, not GitHub's prerelease
        // flag — the tag is what gets stamped into the build itself.
        if (ReleaseVersion.Parse(release.TagName) is not { } version
            || !version.IsOffered(channel)
            || version <= AppVersion.Current
            || (best is not null && version <= best.Version))
        {
            return best;
        }

        if (FindAsset(release) is not { } asset || asset.BrowserDownloadUrl is null)
        {
            // A release with no build for this machine — an osx-only hotfix seen from a booth PC,
            // say. Skip it rather than treating it as the newest thing available.
            return best;
        }

        // GitHub reports asset digests as "sha256:<hex>" when it has one.
        var sha = asset.Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true
            ? asset.Digest["sha256:".Length..]
            : null;

        return new AvailableUpdate(
            version,
            release.TagName ?? version.Tag,
            asset.BrowserDownloadUrl,
            asset.Size,
            sha);
    }

    private static GitHubAsset? FindAsset(GitHubRelease release)
    {
        var wanted = AppPlatform.UpdateAssetName;
        return release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Downloads the asset and records it as pending. Returns the staged file path.</summary>
    public async Task<string> StageAsync(AvailableUpdate update, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(AppPaths.UpdatesDirectory);

        // Same extension as the asset: a macOS bundle arrives as a .zip and the installer has
        // to be able to tell that from a Windows single-file .exe.
        var extension = Path.GetExtension(AppPlatform.UpdateAssetName);
        var target = Path.Combine(AppPaths.UpdatesDirectory, $"SundayReady-{update.Version.Text}{extension}");
        var partial = target + ".part";

        await using (var response = await _client.GetStreamAsync(update.DownloadUrl, cancellationToken).ConfigureAwait(true))
        await using (var file = File.Create(partial))
        {
            await response.CopyToAsync(file, cancellationToken).ConfigureAwait(true);
        }

        // Verify before it is ever a candidate for replacing the running executable.
        var actual = await ComputeSha256Async(partial, cancellationToken).ConfigureAwait(true);
        if (update.Sha256 is not null && !string.Equals(actual, update.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partial);
            throw new InvalidDataException("Downloaded update failed its checksum; discarded.");
        }

        if (update.Size > 0 && new FileInfo(partial).Length != update.Size)
        {
            File.Delete(partial);
            throw new InvalidDataException("Downloaded update was the wrong size; discarded.");
        }

        File.Move(partial, target, overwrite: true);

        WritePending(new PendingUpdate
        {
            Version = update.Version.Text,
            File = target,
            Sha256 = actual,
        });

        return target;
    }

    public static PendingUpdate? ReadPending()
    {
        try
        {
            return File.Exists(AppPaths.PendingUpdateFile)
                ? JsonSerializer.Deserialize<PendingUpdate>(File.ReadAllText(AppPaths.PendingUpdateFile), ChecklistLoader.JsonOptions)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void WritePending(PendingUpdate pending)
    {
        Directory.CreateDirectory(AppPaths.UpdatesDirectory);
        File.WriteAllText(AppPaths.PendingUpdateFile, JsonSerializer.Serialize(pending, ChecklistLoader.JsonOptions));
    }

    public static void ClearPending()
    {
        try
        {
            if (File.Exists(AppPaths.PendingUpdateFile))
            {
                File.Delete(AppPaths.PendingUpdateFile);
            }
        }
        catch (Exception)
        {
            // Nothing useful to do; the version check on the next launch catches it anyway.
        }
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Dispose() => _client.Dispose();

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = new();
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("digest")]
        public string? Digest { get; set; }
    }
}
