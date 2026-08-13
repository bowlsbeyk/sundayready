namespace SundayReady.Models;

/// <summary>
/// A verifier's configuration. <see cref="Kind"/> selects the <c>IVerifier</c>; the remaining
/// fields are kind-specific and only the ones that kind cares about are ever read.
/// <para>
/// This is deliberately one flat class rather than a polymorphic hierarchy: at five kinds a
/// <c>$type</c> discriminator would buy nothing and would complicate the hand-written JSON.
/// </para>
/// </summary>
public sealed class VerifySpec
{
    public const int DefaultMaxAttempts = 10;

    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// How many consecutive failures to absorb as "still polling" before calling the item
    /// failed. Drives the <c>retry 3 of 10</c> sub-line.
    /// </summary>
    public int MaxAttempts { get; set; } = DefaultMaxAttempts;

    /// <summary>processRunning: the process name, with or without a trailing <c>.exe</c>.</summary>
    public string? ProcessName { get; set; }

    /// <summary>httpContains: the URL to GET.</summary>
    public string? Url { get; set; }

    /// <summary>httpContains: the substring the response body must contain.</summary>
    public string? Contains { get; set; }

    /// <summary>internetReachable: host to probe. Optional; the verifier has a default.</summary>
    public string? Host { get; set; }

    /// <summary>audioDevicePresent: substring to look for in audio device names.</summary>
    public string? NameContains { get; set; }

    /// <summary>fileExists: the file or directory path to test.</summary>
    public string? Path { get; set; }

    /// <summary>
    /// The spec as aligned key/value lines for the "what the app tried" block. Built from the
    /// real spec rather than a canned string, so it always describes what actually ran.
    /// </summary>
    public IEnumerable<KeyValuePair<string, string>> DescribeFields()
    {
        yield return new("kind", Kind);

        if (!string.IsNullOrWhiteSpace(ProcessName)) yield return new("processName", ProcessName);
        if (!string.IsNullOrWhiteSpace(Url)) yield return new("url", Url);
        if (!string.IsNullOrEmpty(Contains)) yield return new("contains", $"\"{Contains}\"");
        if (!string.IsNullOrWhiteSpace(Host)) yield return new("host", Host);
        if (!string.IsNullOrWhiteSpace(NameContains)) yield return new("nameContains", $"\"{NameContains}\"");
        if (!string.IsNullOrWhiteSpace(Path)) yield return new("path", Path);
    }
}
