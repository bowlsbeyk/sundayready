using System.Text.Json;
using SundayReady.Models;

namespace SundayReady.Services;

/// <summary>
/// Reads checklist definitions from JSON files in the <c>checklists</c> folder next to the exe.
/// Nothing is embedded as a resource: a checklist must be editable without a rebuild.
/// </summary>
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
            throw new FileNotFoundException($"No checklist file at {path}.", path);
        }

        var definition = JsonSerializer.Deserialize<ChecklistDefinition>(File.ReadAllText(path), JsonOptions);
        if (definition is null)
        {
            throw new InvalidDataException($"{fileName} deserialized to null; it is probably just \"null\".");
        }

        definition.SourceFile = fileName;
        return definition;
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
