namespace Usa.Game.Services.Voice;

public enum SpeechPriority
{
    /// <summary>Idle chatter; a critical line for the same speaker interrupts it.</summary>
    Chatter,
    Normal,

    /// <summary>Jumps ahead of queued non-critical lines and interrupts chatter in progress.</summary>
    Critical,
}

public enum SpeechEndReason
{
    Completed,

    /// <summary>Cut off by a critical line for the same speaker.</summary>
    Interrupted,

    /// <summary><see cref="SpeechPlayer.Stop"/> or <see cref="SpeechPlayer.StopAll"/>.</summary>
    Stopped,
}

/// <summary>One spoken line with its caption. Captions show at playback start (GDD 5.6: speech is never the only channel).</summary>
/// <param name="CrewId">Speaker; each speaker has its own queue and player.</param>
/// <param name="Caption">Resolved caption text (fact tokens already replaced).</param>
/// <param name="Mp3">TTS audio, or null for a caption-only line (TTS failed or text-only fallback); its duration is estimated from the text.</param>
/// <param name="LineId">Optional caller id (e.g. the narrate request id) to correlate events.</param>
/// <param name="SpokenText">What the audio says (a short radio call); the caption may carry more (every value). Null when they are the same.</param>
public sealed record SpeechLine(
    string CrewId,
    string SpeakerName,
    string Caption,
    byte[]? Mp3,
    SpeechPriority Priority = SpeechPriority.Normal,
    string? LineId = null,
    string? SpokenText = null);

/// <param name="Line">The line whose playback ended.</param>
public sealed record SpeechFinished(SpeechLine Line, SpeechEndReason Reason);
