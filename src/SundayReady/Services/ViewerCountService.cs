using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SundayReady.Models;

namespace SundayReady.Services;

/// <summary>
/// A reading of who is watching. Null means "we could not find out", which is different from
/// zero and is rendered as an em-dash rather than a number.
/// </summary>
public sealed record ViewerCounts(int? YouTube, int? Facebook, string? YouTubeTitle, string? Note);

/// <summary>
/// Live viewer counts for the techdesk.
/// <para>
/// Telemetry only. The handoff is explicit that a failed viewer fetch must never affect
/// whether a station reads as ready, so everything here fails soft and returns nulls.
/// </para>
/// <para>
/// YouTube only. Facebook's live metrics need a Page access token from an app that has passed
/// Meta App Review, which is a different order of effort — see the settings screen.
/// </para>
/// </summary>
/// <summary>What a Facebook probe found, including enough detail to diagnose a bad setup.</summary>
public sealed record FacebookProbe(int? Viewers, string? Title, string? Status, string? Note, string? Detail);

public sealed class ViewerCountService : IDisposable
{
    private const string ApiRoot = "https://www.googleapis.com/youtube/v3";

    /// <summary>Name under which the Page token is kept, encrypted, by <see cref="SecretStore"/>.</summary>
    public const string FacebookTokenName = "facebook-page-token";

    /// <summary>
    /// Graph API versions are supported for roughly two years and then start refusing calls.
    /// When Facebook counts stop arriving and the error mentions the version, bump this.
    /// </summary>
    private const string GraphVersion = "v21.0";

    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// The live video id, resolved once and kept. Resolving costs 100 quota units against a
    /// 10,000/day budget, where reading the count costs 1 — so this must not be done per poll.
    /// </summary>
    private string? _resolvedVideoId;

    private string? _resolvedForChannel;

    public async Task<ViewerCounts> ReadAsync(ViewerCountSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.YouTubeApiKey))
        {
            return new ViewerCounts(null, null, null, null);
        }

        try
        {
            var videoId = await ResolveVideoIdAsync(settings, cancellationToken).ConfigureAwait(true);
            if (videoId is null)
            {
                return new ViewerCounts(null, null, null, "Nothing is live on that channel right now.");
            }

            var url = $"{ApiRoot}/videos?part=liveStreamingDetails,snippet&id={Uri.EscapeDataString(videoId)}"
                      + $"&key={Uri.EscapeDataString(settings.YouTubeApiKey)}";

            var response = await _client.GetFromJsonAsync<VideoListResponse>(url, cancellationToken).ConfigureAwait(true);
            var video = response?.Items.FirstOrDefault();

            if (video?.LiveStreamingDetails?.ConcurrentViewers is not { } raw || !int.TryParse(raw, out var viewers))
            {
                // The broadcast ended, or the owner hides the count. Re-resolve next time.
                _resolvedVideoId = null;
                return new ViewerCounts(null, null, video?.Snippet?.Title, "That broadcast is not reporting a live count.");
            }

            var facebook = await ReadFacebookAsync(settings, cancellationToken).ConfigureAwait(true);
            return new ViewerCounts(viewers, facebook, video.Snippet?.Title, null);
        }
        catch (Exception ex)
        {
            var facebook = await ReadFacebookAsync(settings, cancellationToken).ConfigureAwait(true);
            return new ViewerCounts(null, facebook, null, Explain(ex));
        }
    }

    /// <summary>Facebook alone, failing soft to null. Never lets one platform break the other.</summary>
    private async Task<int?> ReadFacebookAsync(ViewerCountSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.FacebookPageId))
        {
            return null;
        }

        var token = SecretStore.Read(FacebookTokenName);
        var probe = await ProbeFacebookAsync(settings.FacebookPageId, token, cancellationToken).ConfigureAwait(true);
        return probe.Viewers;
    }

    private async Task<string?> ResolveVideoIdAsync(ViewerCountSettings settings, CancellationToken cancellationToken)
    {
        // An explicitly configured video wins and costs nothing to "resolve".
        if (!string.IsNullOrWhiteSpace(settings.YouTubeVideoId))
        {
            return ExtractVideoId(settings.YouTubeVideoId);
        }

        if (_resolvedVideoId is not null && _resolvedForChannel == settings.YouTubeChannelId)
        {
            return _resolvedVideoId;
        }

        if (string.IsNullOrWhiteSpace(settings.YouTubeChannelId))
        {
            return null;
        }

        var url = $"{ApiRoot}/search?part=id&eventType=live&type=video"
                  + $"&channelId={Uri.EscapeDataString(settings.YouTubeChannelId)}"
                  + $"&key={Uri.EscapeDataString(settings.YouTubeApiKey!)}";

        var response = await _client.GetFromJsonAsync<SearchResponse>(url, cancellationToken).ConfigureAwait(true);

        _resolvedVideoId = response?.Items.FirstOrDefault()?.Id?.VideoId;
        _resolvedForChannel = settings.YouTubeChannelId;

        return _resolvedVideoId;
    }

    /// <summary>
    /// Reads the Page's current live broadcast and its viewer count.
    /// <para>
    /// Reading your own Page needs no App Review — an app left in Development mode can request
    /// <c>pages_read_engagement</c> from anyone with a role on it, which the church's own admin
    /// has. A Page token derived from a long-lived user token then does not expire.
    /// </para>
    /// <para>
    /// Deliberately verbose about what it found. The likely failures here are configuration,
    /// not code — wrong Page id, a token missing the permission, an expired token, a Graph
    /// version that has aged out — and each needs a different fix.
    /// </para>
    /// </summary>
    public async Task<FacebookProbe> ProbeFacebookAsync(string? pageId, string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(token))
        {
            return new FacebookProbe(null, null, null, "Page id and access token are both needed.", null);
        }

        var url = $"https://graph.facebook.com/{GraphVersion}/{Uri.EscapeDataString(pageId.Trim())}/live_videos"
                  + "?fields=id,status,live_views,title&limit=10"
                  + $"&access_token={Uri.EscapeDataString(token.Trim())}";

        try
        {
            using var response = await _client.GetAsync(url, cancellationToken).ConfigureAwait(true);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true);

            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var message = error.TryGetProperty("message", out var m) ? m.GetString() : "unknown error";
                return new FacebookProbe(null, null, null, $"Facebook said: {message}", null);
            }

            if (!document.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
            {
                return new FacebookProbe(null, null, null, "That Page has no live videos at all.", null);
            }

            foreach (var item in data.EnumerateArray())
            {
                var status = item.TryGetProperty("status", out var s) ? s.GetString() : null;
                if (!string.Equals(status, "LIVE", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;

                if (item.TryGetProperty("live_views", out var views) && views.TryGetInt32(out var count))
                {
                    return new FacebookProbe(count, title, status, null, null);
                }

                // Live, but no count came back — usually the token lacks the permission that
                // exposes it. Say which fields did arrive so the cause is visible.
                var got = string.Join(", ", item.EnumerateObject().Select(p => p.Name));
                return new FacebookProbe(null, title, status,
                    "Live, but Facebook returned no live_views for it.", $"fields returned: {got}");
            }

            var statuses = data.EnumerateArray()
                .Select(i => i.TryGetProperty("status", out var s) ? s.GetString() : "?")
                .Distinct();

            return new FacebookProbe(null, null, null,
                "Nothing is live on that Page right now.", $"recent broadcasts: {string.Join(", ", statuses)}");
        }
        catch (TaskCanceledException)
        {
            return new FacebookProbe(null, null, null, "Facebook did not answer in time.", null);
        }
        catch (Exception ex)
        {
            return new FacebookProbe(null, null, null, ex.Message, null);
        }
    }

    /// <summary>Accepts a bare id or any of the URL shapes someone might paste.</summary>
    public static string ExtractVideoId(string value)
    {
        var trimmed = value.Trim();

        if (!trimmed.Contains('/', StringComparison.Ordinal))
        {
            return trimmed;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            if (query["v"] is { Length: > 0 } fromQuery)
            {
                return fromQuery;
            }

            var last = uri.Segments.LastOrDefault()?.Trim('/');
            if (!string.IsNullOrEmpty(last) && last != "live")
            {
                return last;
            }
        }

        return trimmed;
    }

    private static string Explain(Exception ex) => ex switch
    {
        HttpRequestException http when http.StatusCode == System.Net.HttpStatusCode.Forbidden =>
            "YouTube refused the key — check it is enabled for the YouTube Data API, and that today's quota is not spent.",
        HttpRequestException http when http.StatusCode == System.Net.HttpStatusCode.BadRequest =>
            "YouTube rejected the request — check the API key and channel id.",
        TaskCanceledException => "YouTube did not answer in time.",
        _ => ex.Message,
    };

    public void Dispose() => _client.Dispose();

    private sealed class VideoListResponse
    {
        [JsonPropertyName("items")]
        public List<VideoItem> Items { get; set; } = new();
    }

    private sealed class VideoItem
    {
        [JsonPropertyName("liveStreamingDetails")]
        public LiveDetails? LiveStreamingDetails { get; set; }

        [JsonPropertyName("snippet")]
        public Snippet? Snippet { get; set; }
    }

    private sealed class LiveDetails
    {
        // A string in the API, not a number.
        [JsonPropertyName("concurrentViewers")]
        public string? ConcurrentViewers { get; set; }
    }

    private sealed class Snippet
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }

    private sealed class SearchResponse
    {
        [JsonPropertyName("items")]
        public List<SearchItem> Items { get; set; } = new();
    }

    private sealed class SearchItem
    {
        [JsonPropertyName("id")]
        public SearchId? Id { get; set; }
    }

    private sealed class SearchId
    {
        [JsonPropertyName("videoId")]
        public string? VideoId { get; set; }
    }
}
