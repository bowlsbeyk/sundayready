using System.Text.Json.Serialization;

namespace SundayReady.Models;

/// <summary>
/// One checklist file, rendered as one tab. A station may load several.
/// </summary>
public sealed class ChecklistDefinition
{
    public string Station { get; set; } = string.Empty;

    /// <summary>
    /// Tab label. The station is "Sanctuary Presentation"; its tabs are "Presentation",
    /// "Lyrics &amp; Stage Display", "Shutdown". Falls back to <see cref="Station"/>.
    /// </summary>
    public string? Name { get; set; }

    public List<ChecklistItem> Items { get; set; } = new();

    /// <summary>File this was loaded from. Shown as provenance on the failed-verify screen.</summary>
    [JsonIgnore]
    public string SourceFile { get; set; } = string.Empty;

    [JsonIgnore]
    public string TabLabel => string.IsNullOrWhiteSpace(Name) ? Station : Name;
}
