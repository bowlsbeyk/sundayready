using System.Text.Json;
using SundayReady.Models;

namespace SundayReady.Services;

/// <summary>
/// Loads and saves system maps.
/// <para>
/// Maps live in a <c>maps</c> folder beside the techdesk snapshots, so a building-wide map is
/// written once and every station reads the same one. That is the whole reason it is not stored
/// with the checklists: a checklist describes one station and belongs to it, while a map describes
/// the path <em>between</em> stations and would be wrong the moment two machines disagreed about it.
/// </para>
/// <para>
/// With no share configured it falls back to a local folder, exactly like
/// <see cref="SnapshotStore"/> — so a map can be drawn and tried on one PC before anyone has
/// picked a share.
/// </para>
/// </summary>
public sealed class SystemMapStore
{
    private readonly string _directory;

    public SystemMapStore(string? share = null)
    {
        _directory = System.IO.Path.Combine(SnapshotStore.Resolve(share), "maps");
    }

    public string Directory => _directory;

    public string PathFor(string fileName) => System.IO.Path.Combine(_directory, fileName);

    public bool Exists(string fileName) => File.Exists(PathFor(fileName));

    /// <summary>Every map file, sorted. Missing folder is not an error — it means no maps yet.</summary>
    public IReadOnlyList<string> ListFiles()
    {
        try
        {
            return System.IO.Directory.Exists(_directory)
                ? System.IO.Directory.GetFiles(_directory, "*.json")
                    .Select(System.IO.Path.GetFileName)
                    .Where(f => f is not null)
                    .Select(f => f!)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : Array.Empty<string>();
        }
        catch (Exception)
        {
            // An unreachable share reads as "no maps", which is what the view should show.
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Reads one map. Throws <see cref="ChecklistLoadException"/> on bad JSON so the caller can
    /// report the file and the reason, the same way a broken checklist is reported.
    /// </summary>
    public SystemMap Load(string fileName)
    {
        var path = PathFor(fileName);

        try
        {
            var map = JsonSerializer.Deserialize<SystemMap>(File.ReadAllText(path), ChecklistLoader.JsonOptions)
                ?? throw new ChecklistLoadException(fileName, "the file is empty");

            map.SourceFile = fileName;

            if (string.IsNullOrWhiteSpace(map.Name))
            {
                map.Name = System.IO.Path.GetFileNameWithoutExtension(fileName);
            }

            // Ids are what connections point at, so a hand-written map that omitted them would
            // silently lose its wiring. Fill them in rather than dropping the component.
            foreach (var component in map.Components.Where(c => string.IsNullOrWhiteSpace(c.Id)))
            {
                component.Id = NewId();
            }

            return map;
        }
        catch (JsonException ex)
        {
            throw new ChecklistLoadException(fileName, ex.Message, ex.LineNumber, ex.BytePositionInLine);
        }
        catch (ChecklistLoadException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ChecklistLoadException(fileName, ex.Message);
        }
    }

    public void Save(SystemMap map, string fileName)
    {
        System.IO.Directory.CreateDirectory(_directory);
        File.WriteAllText(
            PathFor(fileName),
            JsonSerializer.Serialize(map, ChecklistWriter.WriteOptions));
    }

    public void Delete(string fileName)
    {
        var path = PathFor(fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Turns a map name into a file name, the same way checklists do it.</summary>
    public static string FileNameFor(string name) => ChecklistWriter.FileNameFor(name);

    /// <summary>
    /// Short, readable, and unique enough for one map. Not a Guid: these end up in a JSON file
    /// people read and edit, and "cam-3-a7f2" is something you can follow with your eye.
    /// </summary>
    public static string NewId(string? label = null)
    {
        var stem = new string((label ?? "node")
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray())
            .Trim('-');

        while (stem.Contains("--", StringComparison.Ordinal))
        {
            stem = stem.Replace("--", "-", StringComparison.Ordinal);
        }

        if (stem.Length == 0)
        {
            stem = "node";
        }

        if (stem.Length > 24)
        {
            stem = stem[..24].TrimEnd('-');
        }

        return $"{stem}-{Guid.NewGuid():N}"[..(stem.Length + 5)];
    }
}
