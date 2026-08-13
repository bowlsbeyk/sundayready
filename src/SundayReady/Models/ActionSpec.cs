namespace SundayReady.Models;

/// <summary>
/// What an <c>action</c> item launches. <see cref="Run"/> may be an exe, a script, or a URL —
/// it is handed to the shell, so anything the operator could double-click works.
/// </summary>
public sealed class ActionSpec
{
    public string Run { get; set; } = string.Empty;

    public string? Args { get; set; }

    /// <summary>Button label. Defaults to "Launch"; set it to "Launch both" and so on.</summary>
    public string? Label { get; set; }

    /// <summary>
    /// Further things launched by the same button, in order. This is how one row opens both
    /// the YouTube and Facebook dashboards without becoming two rows.
    /// </summary>
    public List<ActionSpec> Also { get; set; } = new();
}
