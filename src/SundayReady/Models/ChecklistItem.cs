namespace SundayReady.Models;

/// <summary>
/// The <see cref="ChecklistItem.Type"/> values understood by the app. Anything else is
/// treated as <see cref="Manual"/> so a typo in a JSON file degrades to a checkbox
/// rather than dropping the item on the floor.
/// </summary>
public static class ChecklistItemTypes
{
    public const string Manual = "manual";
    public const string Action = "action";
    public const string Verified = "verified";
}

public sealed class ChecklistItem
{
    public string Label { get; set; } = string.Empty;

    public string Type { get; set; } = ChecklistItemTypes.Manual;

    /// <summary>
    /// Groups items under a divider such as <c>BOOT · 30 MIN BEFORE</c>. A grouping in the
    /// data, not a separate item type. Items with no section land in an unlabelled leading group.
    /// </summary>
    public string? Section { get; set; }

    public ActionSpec? Action { get; set; }

    public VerifySpec? Verify { get; set; }

    /// <summary>
    /// Human troubleshooting copy shown by the failed-verify screen, in order. This is
    /// authored per item by whoever knows the room — it is never generated from the verifier.
    /// </summary>
    public List<string> CheckSteps { get; set; } = new();

    /// <summary>
    /// Optional item-specific remediation offered next to "Retry now" on the failed-verify
    /// screen — e.g. "Reload preset". Without one, only Retry and Override are offered.
    /// </summary>
    public ActionSpec? Remediation { get; set; }

    /// <summary>Button label for <see cref="Remediation"/>.</summary>
    public string? RemediationLabel { get; set; }
}
