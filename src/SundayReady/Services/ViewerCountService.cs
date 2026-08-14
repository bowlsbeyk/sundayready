using System.Net.Http.Json;
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
public sealed class ViewerCountService : IDisposable
{
    private const string ApiRoot = "https://www.googleapis.com/youtube/v3";

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

            return new ViewerCounts(viewers, null, video.Snippet?.Title, null);
        }
        catch (Exception ex)
        {
            return new ViewerCounts(null, null, null, Explain(ex));
        }
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
