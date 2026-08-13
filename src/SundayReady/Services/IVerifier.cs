using SundayReady.Models;

namespace SundayReady.Services;

/// <summary>
/// One attempt at a verifier, in the verifier's own words. <see cref="Result"/> is what the
/// failed-verify screen prints on its <c>result</c> line, so it should read as an observation
/// ("200 OK, body has 3 inputs, no match"), not as a verdict.
/// </summary>
public sealed record VerifyOutcome(bool Passed, string Result, TimeSpan Duration)
{
    public static VerifyOutcome Pass(string result, TimeSpan duration) => new(true, result, duration);

    public static VerifyOutcome Fail(string result, TimeSpan duration) => new(false, result, duration);
}

/// <summary>
/// Checks one kind of real-world condition. Implementations are polled repeatedly and must
/// never throw — a verifier that cannot answer returns a failing outcome that says why.
/// </summary>
public interface IVerifier
{
    /// <summary>Matches the <c>kind</c> field in the checklist JSON.</summary>
    string Kind { get; }

    /// <summary>
    /// True for a kind that is registered but not really implemented yet. The settings screen
    /// draws these dashed so nobody trusts a check that cannot pass.
    /// </summary>
    bool IsStub => false;

    /// <summary>
    /// The verifier's one-line self-description for item sub-lines, e.g. <c>httpContains "Cam 3"</c>.
    /// This is the mechanism made visible; keep it specific.
    /// </summary>
    string Describe(VerifySpec spec);

    Task<VerifyOutcome> CheckAsync(VerifySpec spec, CancellationToken cancellationToken);
}
