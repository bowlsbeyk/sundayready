using System.Diagnostics;
using SundayReady.Models;

namespace SundayReady.Services;

/// <summary>
/// Passes when a GET against the configured URL returns a body containing the configured string.
/// This is how the vMix API check works: GET <c>http://127.0.0.1:8088/api</c>, look for <c>&lt;vmix&gt;</c>.
/// </summary>
public sealed class HttpContainsVerifier : IVerifier, IDisposable
{
    // Short timeout on purpose: this runs on every poll, and a dead vMix must not stall the UI.
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(2) };

    public string Kind => "httpContains";

    public string Describe(VerifySpec spec) => $"httpContains \"{spec.Contains}\"";

    public async Task<VerifyOutcome> CheckAsync(VerifySpec spec, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        TimeSpan Elapsed() => Stopwatch.GetElapsedTime(started);

        if (string.IsNullOrWhiteSpace(spec.Url) || string.IsNullOrEmpty(spec.Contains))
        {
            return VerifyOutcome.Fail("url and contains are both required", Elapsed());
        }

        try
        {
            using var response = await _client.GetAsync(spec.Url, cancellationToken).ConfigureAwait(true);
            var status = $"{(int)response.StatusCode} {response.ReasonPhrase}";

            if (!response.IsSuccessStatusCode)
            {
                return VerifyOutcome.Fail(status, Elapsed());
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true);
            return body.Contains(spec.Contains, StringComparison.OrdinalIgnoreCase)
                ? VerifyOutcome.Pass($"{status}, matched", Elapsed())
                : VerifyOutcome.Fail($"{status}, {body.Length} bytes, no match", Elapsed());
        }
        catch (TaskCanceledException)
        {
            return VerifyOutcome.Fail($"no response within {_client.Timeout.TotalSeconds:0.#}s", Elapsed());
        }
        catch (HttpRequestException ex)
        {
            // Connection refused / DNS failure — usually just "not up yet".
            return VerifyOutcome.Fail(ex.Message, Elapsed());
        }
        catch (Exception ex)
        {
            return VerifyOutcome.Fail(ex.Message, Elapsed());
        }
    }

    public void Dispose() => _client.Dispose();
}
