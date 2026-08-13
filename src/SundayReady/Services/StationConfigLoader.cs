using System.Text.Json;
using SundayReady.Models;

namespace SundayReady.Services;

/// <summary>
/// Works out which station this PC is. <c>station.json</c> next to the exe wins; without one,
/// the hostname decides. Zero-config by default, explicit escape hatch when the default is wrong.
/// </summary>
public sealed class StationConfigLoader
{
    public const string FileName = "station.json";

    private readonly ChecklistLoader _checklists;
    private readonly string _path;

    public StationConfigLoader(ChecklistLoader checklists, string? path = null)
    {
        _checklists = checklists;
        _path = path ?? System.IO.Path.Combine(AppContext.BaseDirectory, FileName);
    }

    public string FilePath => _path;

    public bool FileExists => File.Exists(_path);

    public string DetectedHostname => Environment.MachineName.ToUpperInvariant();

    public StationConfig Load()
    {
        if (File.Exists(_path))
        {
            try
            {
                var config = JsonSerializer.Deserialize<StationConfig>(File.ReadAllText(_path), ChecklistLoader.JsonOptions);
                if (config is not null && config.Checklists.Count > 0)
                {
                    return config;
                }
            }
            catch (Exception)
            {
                // A broken station.json falls through to auto-detect rather than refusing to start.
                // A booth PC that boots into an error dialog is worse than one that guesses.
            }
        }

        return AutoDetect();
    }

    /// <summary>
    /// Hostname auto-detect: load every checklist whose <c>station</c> matches this machine's
    /// name. If none claim it, load everything and take the name from the first file — better
    /// to show the wrong checklist than an empty window.
    /// </summary>
    private StationConfig AutoDetect()
    {
        var host = DetectedHostname;
        var all = new List<ChecklistDefinition>();

        foreach (var file in _checklists.ListFiles())
        {
            try
            {
                all.Add(_checklists.Load(file));
            }
            catch (Exception)
            {
                // Skip files that will not parse; the settings screen is where those get surfaced.
            }
        }

        var mine = all
            .Where(d => string.Equals(d.Station, host, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var chosen = mine.Count > 0 ? mine : all;

        return new StationConfig
        {
            Station = chosen.FirstOrDefault()?.Station ?? host,
            Checklists = chosen.Select(d => d.SourceFile).ToList(),
        };
    }

    public void Save(StationConfig config)
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(config, ChecklistLoader.JsonOptions));
    }
}
