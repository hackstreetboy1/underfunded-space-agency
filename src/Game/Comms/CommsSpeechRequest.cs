using Usa.Simulation.Kernel;

namespace Usa.Game.Comms;

public enum CommsSpeechPriority
{
    Chatter,
    Normal,

    /// <summary>Jumps the speaker's queue and interrupts chatter.</summary>
    Critical,
}

/// <summary>
/// A crew line to speak and caption. The caption is the full final text (fact tokens resolved, every value); only
/// <see cref="SpokenText"/>, a short radio call of at most <see cref="SpokenLine.MaxCharacters"/>, goes to text-to-speech.
/// </summary>
/// <param name="LineId">Deterministic id; also the TTS request id.</param>
/// <param name="VoiceId">TTS voice; empty means caption-only.</param>
/// <param name="Cacheable">Fixed authored text (opener, reason) that the proxy may serve from its phrase cache.</param>
/// <param name="MessageId">The transcript message that shows the same text.</param>
/// <param name="SpokenText">What is said aloud; null means the caption itself.</param>
public sealed record CommsSpeechRequest(
    string LineId,
    CrewId Crew,
    string SpeakerName,
    string Caption,
    string VoiceId,
    CommsSpeechPriority Priority,
    bool Cacheable,
    string MessageId,
    string? SpokenText = null)
{
    /// <summary>The text for text-to-speech.</summary>
    public string Speech => SpokenText ?? Caption;
}
