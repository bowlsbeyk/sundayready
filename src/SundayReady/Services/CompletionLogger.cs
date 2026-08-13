using System.Globalization;
using System.Text;

namespace SundayReady.Services;

/// <summary>The <c>HOW</c> column of the completion log.</summary>
public static class LogHow
{
    public const string Manual = "MANUAL";
    public const string Auto = "AUTO";
    public const string Override = "OVERRIDE";
    public const string Failed = "FAILED";
    public const string Cleared = "CLEARED";
    public const string SignOff = "SIGNOFF";
}

public sealed record LogEntry(
    string Station,
    string Tab,
    string Item,
    string How,
    string? Detail = null,
    string? Initials = null,
    TimeSpan? Duration = null);

/// <summary>One line read back off disk, for the completion-log screen.</summary>
public sealed record LogRecord(
    DateTimeOffset Timestamp,
    string Initials,
    string How,
    string Tab,
    string Item,
    string? Duration,
    string? Detail)
{
    public bool IsFailure => How == LogHow.Failed;

    public bool IsOverride => How == LogHow.Override;

    /// <summary>What the HOW column prints: <c>AUTO 1.4s</c> when the app timed itself.</summary>
    public string HowDisplay => string.IsNullOrEmpty(Duration) ? How : $"{How} {Duration}";
}

/// <summary>
/// Appends a timestamped line per state change, one file per day per station. This is the
/// accountability trail for the Sunday something goes sideways, so it is append-only and
/// never rewritten — including the transitions nobody wants to see, like a passing verifier
/// that started failing.
/// <para>
/// Fields are separated by <c>" | "</c> and every field is always present ("-" when empty) so
/// the file stays both human-readable and parseable back into the log screen. Free text has
/// any embedded separator replaced on the way in.
/// </para>
/// </summary>
public sealed class CompletionLogger
{
    private const string Separator = " | ";

    private const string Empty = "-";

    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss zzz";

    private readonly string _directory;
    private readonly object _gate = new();

    public CompletionLogger(string? directory = null)
    {
        _directory = directory ?? AppPaths.LogsDirectory;
    }

    /// <summary>Set at sign-off or on the first override; stamped into later entries.</summary>
    public string? OperatorInitials { get; set; }

    public string FilePathFor(string station, DateTime day) =>
        Path.Combine(_directory, $"{day:yyyy-MM-dd}_{Slug(station)}.log");

    public void Log(LogEntry entry)
    {
        var now = DateTimeOffset.Now;
        var initials = entry.Initials ?? OperatorInitials;

        var line = new StringBuilder()
            .Append(now.ToString(TimestampFormat, CultureInfo.InvariantCulture)).Append(Separator)
            .Append(Field(initials)).Append(Separator)
            .Append(Field(entry.How)).Append(Separator)
            .Append(Field(entry.Tab)).Append(Separator)
            .Append(Field(entry.Item)).Append(Separator)
            .Append(entry.Duration is { } d ? FormatDuration(d) : Empty).Append(Separator)
            .Append(Field(entry.Detail))
            .ToString();

        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_directory);

                // Encoding.UTF8 writes a BOM when the file is created. Item labels contain
                // em-dashes and quotes, and plenty of Windows tools read a BOM-less file as
                // ANSI and mangle them — not what you want from the record of a service.
                File.AppendAllText(
                    FilePathFor(entry.Station, now.LocalDateTime),
                    line + Environment.NewLine,
                    Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Never let a logging failure stop an operator from working through the checklist.
        }
    }

    /// <summary>Reads today's log for a station. Unparseable lines are skipped, not fatal.</summary>
    public IReadOnlyList<LogRecord> Read(string station, DateTime day)
    {
        var path = FilePathFor(station, day);
        if (!File.Exists(path))
        {
            return Array.Empty<LogRecord>();
        }

        var records = new List<LogRecord>();

        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split(Separator);
                if (parts.Length < 7 || !DateTimeOffset.TryParseExact(
                        parts[0], TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp))
                {
                    continue;
                }

                records.Add(new LogRecord(
                    stamp,
                    parts[1].Trim(),
                    parts[2].Trim(),
                    parts[3].Trim(),
                    parts[4].Trim(),
                    Value(parts[5]),
                    Value(parts[6])));
            }
        }
        catch (Exception)
        {
            // A half-written last line or a locked file just means a shorter list.
        }

        return records;
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalSeconds < 1
            ? $"{duration.TotalMilliseconds:0}ms"
            : $"{duration.TotalSeconds:0.0}s";

    private static string Field(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Empty : value.Replace(Separator, " / ").Trim();

    private static string? Value(string field)
    {
        var trimmed = field.Trim();
        return trimmed is Empty or "" ? null : trimmed;
    }

    private static string Slug(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();

        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
