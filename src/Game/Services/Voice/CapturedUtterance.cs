namespace Usa.Game.Services.Voice;

/// <summary>One push-to-talk recording, ready for <c>AiGateway.TranscribeAsync</c>.</summary>
/// <param name="Wav">16 kHz mono PCM16 WAV.</param>
/// <param name="Truncated">The 15 s cap ended the recording before PTT was released.</param>
/// <param name="PeakMeter">Loudest meter reading (0..1) during the take; near 0 means the mic heard nothing.</param>
public sealed record CapturedUtterance(ReadOnlyMemory<byte> Wav, double DurationSeconds, bool Truncated, float PeakMeter);

public enum CaptureDiscardReason
{
    /// <summary>PTT released too quickly to contain speech.</summary>
    TooShort,

    /// <summary><see cref="MicCapture.CancelCapture"/> was called (e.g. the mission froze).</summary>
    Cancelled,
}
