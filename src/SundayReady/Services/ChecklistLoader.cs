using System.Text.Json;
using SundayReady.Models;

namespace SundayReady.Services;

/// <summary>
/// Reads checklist definitions from JSON files in the <c>checklists</c> folder next to the exe.
/// Nothing is embedded as a resource: a checklist must be editable without a rebuild.
/// </summary>
/// <summary>
/// A checklist file that would not load, with enough detail to go and fix it. Whoever is
/// authoring these is editing JSON by hand, so "line 12, position 5" is the difference
/// between a two-second fix and a hunt.
/// </summary>
public sealed class ChecklistLoadException : Exception
{
    public ChecklistLoadException(string fileName, string reason, long? line = null, long? position = null)
        : base(Describe(fileName, reason, line, position))
    {
        FileName = fileName;
        Reason = reason;
        Line = line;
        Position = position;
    }

    public string FileName { get; }

    public string Reason { get; }

    public long? Line { get; }

    public long? Position { get; }

    private static string Describe(string fileName, string reason, long? line, long? position) =>
        line is null
            ? $"{fileName} — {reason}"
            : $"{fileName} line {line}, position {position} — {reason}";
}

public sealed class ChecklistLoader
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    private readonly string _directory;

    public ChecklistLoader(string? directory = null)
    {
        _directory = directory ?? AppPaths.ChecklistsDirectory;
    }

    public string Directory => _directory;

    /// <summary>
    /// Loads one checklist by file name. Throws on a missing or malformed file — the caller
    /// shows the operator what went wrong rather than starting with a silently empty list.
    /// </summary>
    public ChecklistDefinition Load(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        if (!File.Exists(path))
        {
            throw new ChecklistLoadException(fileName, "file not found");
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            // Usually an editor still holding the file mid-save. The caller retries.
            throw new ChecklistLoadException(fileName, $"could not be read — {ex.Message}");
        }

        ChecklistDefinition? definition;
        try
        {
            definition = JsonSerializer.Deserialize<ChecklistDefinition>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            // LineNumber and BytePositionInLine are zero-based; editors count from one.
            throw new ChecklistLoadException(
                fileName,
                CleanJsonMessage(ex.Message),
                ex.LineNumber is { } line ? line + 1 : null,
                ex.BytePositionInLine is { } position ? position + 1 : null);
        }

        if (definition is null)
        {
            throw new ChecklistLoadException(fileName, "the file contains only \"null\"");
        }

        definition.SourceFile = fileName;
        return definition;
    }

    /// <summary>
    /// System.Text.Json appends its own "Path: $ | LineNumber: 3 | ..." trailer, which we
    /// already render properly. Strip it so the message reads as one sentence.
    /// </summary>
    private static string CleanJsonMessage(string message)
    {
        var trailer = message.IndexOf(" Path: ", StringComparison.Ordinal);
        var cleaned = trailer < 0 ? message : message[..trailer];
        return cleaned.TrimEnd('.', ' ');
    }

    /// <summary>Every checklist file in the folder, in name order.</summary>
    public IReadOnlyList<string> ListFiles()
    {
        if (!System.IO.Directory.Exists(_directory))
        {
            return Array.Empty<string>();
        }

        return System.IO.Directory
            .EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.Equals(name, StationConfigLoader.FileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    public IReadOnlyList<ChecklistDefinition> LoadAll() =>
        ListFiles().Select(Load).ToList();
}
