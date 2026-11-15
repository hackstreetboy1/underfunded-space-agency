namespace Usa.Game.Guidance;

/// <summary>
/// Reasons <see cref="TipRephraseValidator"/> discards a rephrasing (the authored text stays). Text guardrail failures
/// reuse <c>AiRejectionCodes</c> (<c>forbidden_word</c>, <c>number_with_unit</c>, <c>component_id_in_text</c>, fact tokens).
/// </summary>
public static class TipRejectionCodes
{
    public const string TipMismatch = "tip_mismatch";
    public const string StageMismatch = "stage_mismatch";
    public const string FactNotAllowed = "fact_not_allowed";
    public const string ActionNotAllowed = "action_not_allowed";
    public const string EmptyText = "empty_text";
    public const string TextTooLong = "text_too_long";
    public const string NewAction = "new_action";

    /// <summary>An equipment designator ("T8", "ring B", "Aux 3") the authored text does not name.</summary>
    public const string NewDesignator = "new_designator";

    /// <summary>The card opens with the ground's call sign ("CAPCOM, ..."): it is a written note, not a radio call.</summary>
    public const string CallSignOpener = "call_sign_opener";
}
