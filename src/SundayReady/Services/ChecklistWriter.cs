using System.Collections;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SundayReady.Models;

namespace SundayReady.Services;

/// <summary>
/// Writes checklist files back out from the editor.
/// <para>
/// Files written here are machine-generated, so they lose any comments a human had put in
/// them. That is the trade for editing in the app; the shipped samples say so.
/// </para>
/// </summary>
public sealed class ChecklistWriter
{
    /// <summary>
    /// Deliberately not <see cref="ChecklistLoader.JsonOptions"/>: reading should be tolerant,
    /// writing should be tidy. Nulls and empty collections are dropped so a simple manual item
    /// stays two lines rather than carrying eight empty fields.
    /// </summary>
    public static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { OmitEmptyCollections },
        },
    };

    private readonly string _directory;

    public ChecklistWriter(string? directory = null)
    {
        _directory = directory ?? AppPaths.ChecklistsDirectory;
    }

    public string Directory => _directory;

    public string PathFor(string fileName) => Path.Combine(_directory, fileName);

    public void Save(ChecklistDefinition definition, string fileName)
    {
        System.IO.Directory.CreateDirectory(_directory);

        var json = JsonSerializer.Serialize(definition, WriteOptions);

        // Written atomically: the station is watching this folder and would otherwise get a
        // reload fired at a half-written file.
        var target = PathFor(fileName);
        var temporary = target + ".tmp";

        File.WriteAllText(temporary, json, new UTF8Encoding(false));
        File.Move(temporary, target, overwrite: true);
    }

    public void Delete(string fileName)
    {
        var path = PathFor(fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public bool Exists(string fileName) => File.Exists(PathFor(fileName));

    /// <summary>Turns a tab name into a usable file name: "Go Live" becomes go-live.json.</summary>
    public static string FileNameFor(string tabName)
    {
        var slug = new string(tabName.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray());

        slug = string.Join('-', slug.Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrEmpty(slug) ? "checklist.json" : $"{slug}.json";
    }

    private static void OmitEmptyCollections(JsonTypeInfo info)
    {
        foreach (var property in info.Properties)
        {
            if (typeof(ICollection).IsAssignableFrom(property.PropertyType))
            {
                property.ShouldSerialize = (_, value) => value is ICollection { Count: > 0 };
            }
        }
    }
}
