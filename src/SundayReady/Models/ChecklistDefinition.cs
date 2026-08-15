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

    /// <summary>
    /// Whether this checklist has to be finished before the station counts as ready.
    /// <para>
    /// A shutdown list is the reason this exists: it is real work on a real tab, but it
    /// happens after the service. Leaving it in the gate would mean a station could never be
    /// ready before the service it is getting ready for.
    /// </para>
    /// </summary>
    public bool CountsTowardReady { get; set; } = true;

    /// <summary>
    /// Open this checklist when the operator presses "Service finished".
    /// <para>
    /// Separate from <see cref="CountsTowardReady"/> on purpose: a station can have several
    /// lists that sit outside the gate — post-show and shutdown, say — and only one of them
    /// is the one to put in front of someone the moment the service ends.
    /// </para>
    /// </summary>
    public bool OpenAfterService { get; set; }

    public List<ChecklistItem> Items { get; set; } = new();

    /// <summary>File this was loaded from. Shown as provenance on the failed-verify screen.</summary>
    [JsonIgnore]
    public string SourceFile { get; set; } = string.Empty;

    [JsonIgnore]
    public string TabLabel => string.IsNullOrWhiteSpace(Name) ? Station : Name;
}
