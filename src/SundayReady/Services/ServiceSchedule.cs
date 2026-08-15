using System.Globalization;
using SundayReady.Models;

namespace SundayReady.Services;

/// <summary>
/// One occurrence of a service on a specific day. <see cref="Key"/> identifies it in saved
/// state, so the app can tell "still the 9am" from "now preparing for the 11am".
/// </summary>
public sealed record ServiceOccurrence(DateTime Start, string Key)
{
    public string Display => Start.ToString("h:mm tt", CultureInfo.InvariantCulture).ToUpperInvariant();
}

/// <summary>
/// Works out which service the station is currently preparing for, from a list of service
/// times.
/// <para>
/// A station is preparing for a service from <c>lead</c> minutes before it starts until
/// <c>lead</c> minutes before the next one. That rollover is when the checklist starts again
/// — which is what a PC that is never switched off needs, since it will never see a restart.
/// </para>
/// </summary>
public sealed class ServiceSchedule
{
    public const int DefaultLeadMinutes = 90;

    private readonly List<TimeOnly> _times;
    private readonly TimeSpan _lead;

    public ServiceSchedule(ServiceTimes? service)
    {
        _times = ParseTimes(service).Distinct().OrderBy(t => t).ToList();

        var minutes = service?.ResetLeadMinutes ?? DefaultLeadMinutes;
        _lead = TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 24 * 60));
    }

    public bool HasTimes => _times.Count > 0;

    public IReadOnlyList<TimeOnly> Times => _times;

    public TimeSpan Lead => _lead;

    /// <summary>
    /// The service being prepared for right now, or null when no times are configured.
    /// </summary>
    public ServiceOccurrence? Current(DateTime now)
    {
        if (_times.Count == 0)
        {
            return null;
        }

        // Yesterday and tomorrow are included so the answer is right either side of midnight —
        // an evening service with a long lead belongs to the day it starts, not the day the
        // preparation began.
        var candidates = new List<DateTime>();
        for (var offset = -1; offset <= 1; offset++)
        {
            var day = now.Date.AddDays(offset);
            candidates.AddRange(_times.Select(t => day.Add(t.ToTimeSpan())));
        }

        candidates.Sort();

        // The one whose preparation window has opened most recently.
        DateTime? chosen = null;
        foreach (var start in candidates)
        {
            if (start - _lead <= now)
            {
                chosen = start;
            }
        }

        chosen ??= candidates.First();
        return new ServiceOccurrence(chosen.Value, chosen.Value.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture));
    }

    /// <summary>How long until that service starts. Negative once it has begun.</summary>
    public TimeSpan? TimeUntil(DateTime now) =>
        Current(now) is { } occurrence ? occurrence.Start - now : null;

    /// <summary>All of today's times, for the sub-line under the countdown.</summary>
    public string Describe()
    {
        if (_times.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(" · ", _times.Select(t =>
            t.ToString("h:mm tt", CultureInfo.InvariantCulture).ToUpperInvariant()));
    }

    private static IEnumerable<TimeOnly> ParseTimes(ServiceTimes? service)
    {
        if (service is null)
        {
            yield break;
        }

        // The list is the model; the old single startsAt is still honoured so existing
        // station.json files keep working untouched.
        foreach (var raw in service.Starts.Concat(new[] { service.StartsAt }))
        {
            if (!string.IsNullOrWhiteSpace(raw) && TimeOnly.TryParse(raw.Trim(), out var parsed))
            {
                yield return parsed;
            }
        }
    }
}
