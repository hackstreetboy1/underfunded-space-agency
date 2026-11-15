namespace Usa.Game.Advisor;

/// <summary>Reasons <see cref="AdvisorAnswerValidator"/> discards an answer, besides the shared <c>AiRejectionCodes</c>.</summary>
public static class AdvisorRejectionCodes
{
	public const string EmptyAnswer = "empty_answer";
	public const string AnswerTooLong = "answer_too_long";
	public const string UnknownSource = "unknown_source";
	public const string Recommendation = "recommendation";
	public const string NumberNotInExcerpts = "number_not_in_excerpts";
	public const string Timeout = "advisor_timeout";

	/// <summary>Angle brackets, fact tokens or BBCode tags: answers are plain text.</summary>
	public const string Markup = "markup";

	/// <summary>The answer opens with the ground's call sign ("CAPCOM, ..."): it is reference text, not a radio call.</summary>
	public const string CallSignOpener = "call_sign_opener";
}
