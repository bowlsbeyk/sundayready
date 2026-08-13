using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SundayReady.Services;

/// <summary>A release newer than the running build, and the asset to fetch.</summary>
public sealed record AvailableUpdate(Version Version, string Tag, string DownloadUrl, long Size, string? Sha256);

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
/// <see cref="UpdateInstaller"/>'s job, at the next launch — nothing is ever swapped under an
/// operator mid-service.
/// <para>
/// The repository is public, so no token is involved and none of this touches credentials.
/// </para>
/// </summary>
public sealed class UpdateService : IDisposable
{
    public const string DefaultRepository = "bowlsbeyk/SundayReady";

    /// <summary>Matches the asset the release workflow uploads.</summary>
    private const string AssetName = "SundayReady-win-x64.exe";

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
    /// Returns the latest release if it is newer than the running build, otherwise null.
    /// Network failure is not an error worth surfacing loudly — it throws, and the caller
    /// records it as a status line rather than bothering the operator.
    /// </summary>
    public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{_repository}/releases/latest";
        var release = await _client.GetFromJsonAsync<GitHubRelease>(url, cancellationToken).ConfigureAwait(true);

        var version = AppVersion.ParseTag(release?.TagName);
        if (release is null || version is null || version <= AppVersion.Current)
        {
            return null;
        }

        var asset = release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, AssetName, StringComparison.OrdinalIgnoreCase));

        if (asset?.BrowserDownloadUrl is null)
        {
            return null;
        }

        // GitHub reports asset digests as "sha256:<hex>" when it has one.
        var sha = asset.Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true
            ? asset.Digest["sha256:".Length..]
            : null;

        return new AvailableUpdate(version, release.TagName ?? string.Empty, asset.BrowserDownloadUrl, asset.Size, sha);
    }

    /// <summary>Downloads the asset and records it as pending. Returns the staged file path.</summary>
    public async Task<string> StageAsync(AvailableUpdate update, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(AppPaths.UpdatesDirectory);

        var target = Path.Combine(AppPaths.UpdatesDirectory, $"SundayReady-{update.Version.ToString(3)}.exe");
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
            Version = update.Version.ToString(3),
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
