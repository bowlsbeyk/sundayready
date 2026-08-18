using System.Text.Json;
using SundayReady.Models;

namespace SundayReady.Services;

/// <summary>
/// Loads and saves system maps, and the connection-type registry every map reads from.
/// <para>
/// Maps live in a <c>maps</c> folder beside the techdesk snapshots, so a building-wide map is
/// written once and every station reads the same one. That is the whole reason it is not stored
/// with the checklists: a checklist describes one station and belongs to it, while a map describes
/// the path <em>between</em> stations and would be wrong the moment two machines disagreed.
/// </para>
/// <para>
/// With no share configured it falls back to a local folder, exactly like
/// <see cref="SnapshotStore"/> — so a map can be drawn and tried on one PC before anyone has
/// picked a share.
/// </para>
/// </summary>
public sealed class SystemMapStore
{
    /// <summary>The default map file, per the handoff. Extra maps sit beside it.</summary>
    public const string DefaultFileName = "system-map.json";

    /// <summary>Custom connection types, shared by all maps. Built-ins never get written here.</summary>
    public const string TypesFileName = "connection-types.json";

    private readonly string _directory;

    public SystemMapStore(string? share = null)
    {
        _directory = System.IO.Path.Combine(SnapshotStore.Resolve(share), "maps");
    }

    public string Directory => _directory;

    public string PathFor(string fileName) => System.IO.Path.Combine(_directory, fileName);

    public bool Exists(string fileName) => File.Exists(PathFor(fileName));

    /// <summary>Every map file, sorted, the default first. Missing folder means no maps yet.</summary>
    public IReadOnlyList<string> ListFiles()
    {
        try
        {
            return System.IO.Directory.Exists(_directory)
                ? System.IO.Directory.GetFiles(_directory, "*.json")
                    .Select(System.IO.Path.GetFileName)
                    .Where(f => f is not null && !string.Equals(f, TypesFileName, StringComparison.OrdinalIgnoreCase))
                    .Select(f => f!)
                    .OrderBy(f => string.Equals(f, DefaultFileName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
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
    /// Built-in types plus whatever custom ones the church has added, in that order. The
    /// registry is shared by all maps — a type is a vocabulary word, not a per-map setting.
    /// </summary>
    public IReadOnlyList<MapConnectionType> LoadTypes()
    {
        var types = new List<MapConnectionType>(MapConnectionTypes.BuiltIn);

        try
        {
            var path = PathFor(TypesFileName);
            if (File.Exists(path))
            {
                var custom = JsonSerializer.Deserialize<List<MapConnectionType>>(
                    File.ReadAllText(path), ChecklistLoader.JsonOptions);

                // A custom type shadowing a built-in id is taken as intended: the church
                // wanted different flow speed or colour for XLR, and saying no would just
                // push them into creating "xlr2".
                foreach (var type in custom ?? new List<MapConnectionType>())
                {
                    type.BuiltIn = false;
                    types.RemoveAll(t => string.Equals(t.Id, type.Id, StringComparison.OrdinalIgnoreCase));
                    types.Add(type);
                }
            }
        }
        catch (Exception)
        {
            // A broken registry file must not take the maps down; the built-ins still work.
        }

        return types;
    }

    public void SaveTypes(IEnumerable<MapConnectionType> customTypes)
    {
        System.IO.Directory.CreateDirectory(_directory);
        File.WriteAllText(
            PathFor(TypesFileName),
            JsonSerializer.Serialize(customTypes.Where(t => !t.BuiltIn), ChecklistWriter.WriteOptions));
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

            foreach (var device in map.Devices)
            {
                // Ids are what connections point at; a hand-written map that omitted them
                // would silently lose its wiring. Fill in rather than drop.
                if (string.IsNullOrWhiteSpace(device.Id))
                {
                    device.Id = NewId(device.Label);
                }

                // Tier defaulting is an honesty rule: a device with a check is verified, and a
                // device without one is a guess — and a guess is drawn as one.
                if (string.IsNullOrWhiteSpace(device.Tier))
                {
                    device.Tier = device.Verify is null ? MapTiers.Inferred : MapTiers.Verified;
                }
            }

            foreach (var connection in map.Connections)
            {
                if (string.IsNullOrWhiteSpace(connection.Id))
                {
                    connection.Id = NewId($"{connection.From}-{connection.To}");
                }

                // The seed keeps each wire's animation speed stable across restarts. Derived
                // from the id so a hand-written file gets its jitter without writing numbers.
                if (connection.FlowSeed == 0)
                {
                    connection.FlowSeed = StableHash(connection.Id);
                }
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
    /// Short, readable, unique enough for one map. Not a Guid: these end up in a JSON file
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

    /// <summary>
    /// Deterministic, platform-stable hash. Not <c>string.GetHashCode</c>, which is randomised
    /// per process — the whole point of the seed is surviving restarts.
    /// </summary>
    public static int StableHash(string text)
    {
        unchecked
        {
            var hash = 23;
            foreach (var c in text)
            {
                hash = (hash * 31) + c;
            }

            return hash == 0 ? 1 : Math.Abs(hash);
        }
    }
}
