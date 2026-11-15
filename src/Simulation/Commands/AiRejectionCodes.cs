namespace Usa.Simulation.Commands;

/// <summary>Reasons <see cref="AiResponseValidator"/> discards a model response.</summary>
public static class AiRejectionCodes
{
    public const string InvalidKind = "invalid_kind";
    public const string UnknownIntent = "unknown_intent";
    public const string UnknownCrewMember = "unknown_crew_member";
    public const string CrewMemberDead = "crew_member_dead";
    public const string MissingAction = "missing_action";
    public const string UnknownAction = "unknown_action";
    public const string ActionNotAllowedInPhase = "action_not_allowed_in_phase";
    public const string InvalidParams = "invalid_params";
    public const string UnknownFact = "unknown_fact";
    public const string FactNotKnown = "fact_not_known";
    public const string FactTokenNotInRefs = "fact_token_not_in_refs";
    public const string MalformedFactToken = "malformed_fact_token";
    public const string NumberWithUnit = "number_with_unit";
    public const string ForbiddenWord = "forbidden_word";
    public const string ComponentIdInText = "component_id_in_text";
    public const string MissingClarificationQuestion = "missing_clarification_question";
}
