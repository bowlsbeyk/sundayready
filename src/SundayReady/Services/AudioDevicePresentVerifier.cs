using SundayReady.Models;

namespace SundayReady.Services;

/// <summary>
/// Passes when an audio device whose name contains the configured substring is present.
/// </summary>
public sealed class AudioDevicePresentVerifier : IVerifier
{
    public string Kind => "audioDevicePresent";

    public bool IsStub => true;

    public string Describe(VerifySpec spec) => $"audioDevicePresent \"{spec.NameContains}\"";

    public Task<VerifyOutcome> CheckAsync(VerifySpec spec, CancellationToken cancellationToken)
    {
        // TODO: Implement. Enumerating audio endpoints needs Windows Core Audio (MMDeviceEnumerator),
        // which means either a P/Invoke layer or a package such as NAudio. Deferred until the
        // signal-chain questions are answered, since what we match on (Dante virtual soundcard,
        // a USB interface, an NDI source) depends on how audio actually reaches vMix.
        return Task.FromResult(VerifyOutcome.Fail("audioDevicePresent is not implemented yet", TimeSpan.Zero));
    }
}
