namespace Usa.Game.Advisor;

/// <summary>An encyclopedia entry an answer drew on, as the sheet lists it.</summary>
public sealed record AdvisorSource(string EntryId, string Title, string EraLabel);

/// <summary>What the engineers say: a validated model answer, or the deterministic encyclopedia fallback.</summary>
/// <param name="Sources">Entries the answer drew on, in the order cited; empty when the reference does not cover the question.</param>
/// <param name="IsFallback">True when the text is built from the best excerpt because the model failed, timed out or was rejected.</param>
public sealed record AdvisorAnswer(string Text, IReadOnlyList<AdvisorSource> Sources, bool IsFallback);

public enum AdvisorReplyKind
{
	/// <summary><see cref="AdvisorReply.Answer"/> holds a validated or fallback answer.</summary>
	Answered,

	/// <summary>No encyclopedia entry matches the question; nothing was sent.</summary>
	NotCovered,

	/// <summary>Ignored: the question was empty.</summary>
	Empty,

	/// <summary>Ignored: another question is still waiting for its answer.</summary>
	Busy,

	/// <summary>Not asked again: the same question came within the duplicate window. <see cref="AdvisorReply.Answer"/> holds
	/// the answer that question got, or null when nothing covered it.</summary>
	Duplicate,

	/// <summary>The caller cancelled.</summary>
	Cancelled,
}

/// <param name="Detail">Why a fallback was used (an error or rejection code) for logs; never shown to the player.</param>
public sealed record AdvisorReply(AdvisorReplyKind Kind, AdvisorAnswer? Answer = null, string? Detail = null);
