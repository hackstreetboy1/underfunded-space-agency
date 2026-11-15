using Usa.Simulation.Kernel;

namespace Usa.Game.Comms;

/// <summary>A speech-to-text result.</summary>
/// <param name="Confidence">Mean word probability (0..1).</param>
/// <param name="MinWordConfidence">Lowest word probability (0..1).</param>
/// <param name="LowConfidenceWords">Words the service flagged, in spoken order.</param>
public sealed record VoiceTranscript(string Text, double Confidence, double MinWordConfidence, IReadOnlyList<string> LowConfidenceWords);

/// <summary>A transcript the player must check before it is sent (UI.md 3.5: placed in the field, flagged words highlighted).</summary>
public sealed record TranscriptReview(VoiceTranscript Transcript, CrewId? Addressee);
