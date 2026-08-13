using SundayReady.Models;

namespace SundayReady.Services;

/// <summary>
/// Maps the <c>kind</c> field in checklist JSON to an <see cref="IVerifier"/>. An unknown kind
/// resolves to nothing and that one item degrades — the file still loads. A checklist must
/// never be refused wholesale over a single unrecognised verifier.
/// </summary>
public sealed class VerifierRegistry : IDisposable
{
    private readonly Dictionary<string, IVerifier> _byKind;

    public VerifierRegistry(IEnumerable<IVerifier> verifiers)
    {
        _byKind = verifiers.ToDictionary(v => v.Kind, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every kind the app ships with. <c>audioDevicePresent</c> is still a stub.</summary>
    public static VerifierRegistry CreateDefault() => new(new IVerifier[]
    {
        new ProcessRunningVerifier(),
        new HttpContainsVerifier(),
        new FileExistsVerifier(),
        new InternetReachableVerifier(),
        new AudioDevicePresentVerifier(),
    });

    public IReadOnlyCollection<string> Kinds => _byKind.Keys;

    public IReadOnlyCollection<IVerifier> All => _byKind.Values;

    public bool Knows(string? kind) =>
        !string.IsNullOrWhiteSpace(kind) && _byKind.ContainsKey(kind);

    public bool TryGet(VerifySpec spec, out IVerifier verifier)
    {
        verifier = null!;
        return !string.IsNullOrWhiteSpace(spec.Kind) && _byKind.TryGetValue(spec.Kind, out verifier!);
    }

    public void Dispose()
    {
        foreach (var verifier in _byKind.Values.OfType<IDisposable>())
        {
            verifier.Dispose();
        }
    }
}
