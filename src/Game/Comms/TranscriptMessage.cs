using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;

namespace Usa.Game.Comms;

/// <summary>Transcript message types (UI.md 3.5).</summary>
public enum TranscriptMessageKind
{
    PlayerText,
    PlayerVoice,
    Crew,
    System,

    /// <summary>An automatic crew report (the UI shows a "REPORT" badge).</summary>
    ObservationReport,

    /// <summary>The crew asks one clarifying question; <see cref="TranscriptMessage.Options"/> become quick replies.</summary>
    Clarification,

    /// <summary>The crew refuses; <see cref="TranscriptMessage.ReasonText"/> is the validator's reason in words.</summary>
    Refusal,

    /// <summary>"Okafor is responding…" while a request is in flight or held; removed when it resolves.</summary>
    Pending,
}

/// <summary>One transcript line. Immutable; ids are stable for the session and never reused.</summary>
/// <param name="Id">Assigned by <see cref="Transcript.Append"/>.</param>
/// <param name="Text">What the player reads (crew text with fact values already resolved).</param>
public sealed record TranscriptMessage(string Id, TranscriptMessageKind Kind, MissionTime Met, string Text)
{
    public CrewId? Speaker { get; init; }

    public string? SpeakerName { get; init; }

    /// <summary>
    /// Who a player message went to: the crew member addressed when it was sent (for voice, when the recording was handed
    /// to transcription, not when the talk key went down); null for the whole crew and for non-player messages.
    /// </summary>
    public CrewId? Addressee { get; init; }

    /// <summary>The command / AI request id this message belongs to, for "edit &amp; resend", cancel and correlation.</summary>
    public string? RequestId { get; init; }

    /// <summary>Crew text before fact resolution (with <c>&lt;FACT:id&gt;</c> tokens). Only this form goes back to the AI.</summary>
    public string? Template { get; init; }

    /// <summary>
    /// What the crew member says aloud (sent to text-to-speech): a short radio call with the headline only, while
    /// <see cref="Text"/> keeps every value. Null for messages nobody speaks.
    /// </summary>
    public string? SpokenText { get; init; }

    /// <summary>Resolved facts in text order, for the UI's fact chips (value, MET, stale badge).</summary>
    public IReadOnlyList<ResolvedFact> Facts { get; init; } = [];

    /// <summary>Quick-reply options of a <see cref="TranscriptMessageKind.Clarification"/>.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>
    /// The speaker's one-tap offer, accepted with <see cref="CommsOrchestrator.AcceptOffer"/>: a <see cref="Comms.ReadOffer"/>
    /// (GDD 5.2 "offers to check") or an <see cref="ActionOffer"/> ("Want us to go ahead?" after a recommendation request).
    /// </summary>
    public CrewOffer? Offer { get; init; }

    public string? ReasonCode { get; init; }

    public string? ReasonText { get; init; }

    public bool IsQuickCommand { get; init; }

    /// <summary>Speech-to-text confidence of a voice message.</summary>
    public double? Confidence { get; init; }

    /// <summary>A message to pass to <see cref="Transcript.Append"/>, which assigns the id.</summary>
    public static TranscriptMessage Draft(TranscriptMessageKind kind, MissionTime met, string text) => new("", kind, met, text);
}

public enum TranscriptChangeKind
{
    Added,
    Removed,
}

public sealed record TranscriptChange(TranscriptChangeKind Kind, TranscriptMessage Message);
