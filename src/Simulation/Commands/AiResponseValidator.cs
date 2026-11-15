using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Commands;

/// <summary>
/// Checks a model response before anything acts on it (SCRIPTING 13.2): intent kind, phase allowlist,
/// parameter schema, crew roster and alive status, facts known to the speaker, fact tokens, and text
/// guardrails. Collects every problem; any rejection means the caller falls back to its deterministic template.
/// A valid command still goes through <see cref="CommandProcessor.Submit"/> for full validation.
/// </summary>
public sealed class AiResponseValidator(
    ActionCatalog catalog,
    IStateQuery state,
    ICrewDirectory crew,
    IKnownFacts knownFacts,
    AiGuardrails guardrails)
{
    public AiValidationResult Validate(AiIntent intent, AiValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(context);
        var rejections = new List<AiRejection>();

        var kind = ParseKind(intent.Kind);
        if (kind is null) rejections.Add(new AiRejection(AiRejectionCodes.InvalidKind, intent.Kind));
        else if (kind == AiIntentKind.Unknown) rejections.Add(new AiRejection(AiRejectionCodes.UnknownIntent));

        var named = ResolveSpeaker(intent.CrewMember, context.Addressee, rejections, out var wholeCrew);
        var refs = ParseFactRefs(intent.FactRefs ?? [], rejections);
        var speaker = wholeCrew ? PickCrewSpeaker(refs) : named;
        var facts = CheckFactsKnown(refs, speaker, rejections);

        CheckText(intent.DialogueTemplate, "dialogue_template", facts, rejections);
        if (intent.ClarificationQuestion is { } question) CheckText(question, "clarification_question", facts, rejections);
        foreach (var option in intent.ClarificationOptions ?? []) CheckText(option, "clarification_options", facts, rejections);

        CommandRequest? command = null;
        if (kind == AiIntentKind.Command)
        {
            command = BuildCommand(intent, context, wholeCrew ? CommandRequest.AnyCrew : named, rejections);
        }
        else if (!string.IsNullOrEmpty(intent.ActionId) && FindAction(intent.ActionId) is null)
        {
            rejections.Add(new AiRejection(AiRejectionCodes.UnknownAction, intent.ActionId));
        }

        if (kind == AiIntentKind.Clarify && string.IsNullOrWhiteSpace(intent.ClarificationQuestion))
            rejections.Add(new AiRejection(AiRejectionCodes.MissingClarificationQuestion));

        if (rejections.Count > 0) return new AiValidationResult(null, rejections);

        var validated = new ValidatedIntent(
            kind!.Value,
            speaker,
            command,
            facts,
            intent.DialogueTemplate,
            intent.ClarificationQuestion,
            intent.ClarificationOptions ?? [],
            intent.RequiresConfirmation);
        return new AiValidationResult(validated, []);
    }

    private static AiIntentKind? ParseKind(string kind) => kind switch
    {
        "command" => AiIntentKind.Command,
        "question" => AiIntentKind.Question,
        "clarify" => AiIntentKind.Clarify,
        "recommendation_request" => AiIntentKind.RecommendationRequest,
        "chat" => AiIntentKind.Chat,
        "unknown" => AiIntentKind.Unknown,
        _ => null,
    };

    private CrewId? ResolveSpeaker(string? crewMember, CrewId? addressee, List<AiRejection> rejections, out bool wholeCrew)
    {
        wholeCrew = false;
        if (string.IsNullOrEmpty(crewMember))
        {
            if (addressee is not { } addressed || addressed == CommandRequest.AnyCrew || !crew.IsMember(addressed))
            {
                wholeCrew = true;
                return null;
            }
            return CheckAlive(addressed, rejections);
        }

        if (string.Equals(crewMember, CommandRequest.AnyCrew.Value, StringComparison.Ordinal))
        {
            wholeCrew = true;
            return null;
        }

        if (!IdParsing.TryCreate(crewMember, v => new CrewId(v), out var id) || !crew.IsMember(id))
        {
            rejections.Add(new AiRejection(AiRejectionCodes.UnknownCrewMember, crewMember));
            return null;
        }
        return CheckAlive(id, rejections);
    }

    private CrewId? CheckAlive(CrewId id, List<AiRejection> rejections)
    {
        if (crew.IsAlive(id)) return id;
        rejections.Add(new AiRejection(AiRejectionCodes.CrewMemberDead, id.Value));
        return null;
    }

    private static List<(string Raw, FactId Fact)> ParseFactRefs(IReadOnlyList<string> refs, List<AiRejection> rejections)
    {
        var parsed = new List<(string, FactId)>(refs.Count);
        foreach (var raw in refs)
        {
            if (IdParsing.TryCreate(raw, v => new FactId(v), out var fact)) parsed.Add((raw, fact));
            else rejections.Add(new AiRejection(AiRejectionCodes.UnknownFact, raw));
        }
        return parsed;
    }

    /// <summary>
    /// A whole-crew answer is still spoken by one member (SCRIPTING 13.2): the first living member in roster
    /// order who knows every referenced fact, or else the first living member, whose gaps are then rejected.
    /// </summary>
    private CrewId? PickCrewSpeaker(List<(string Raw, FactId Fact)> refs)
    {
        CrewId? firstAlive = null;
        foreach (var member in crew.Members)
        {
            if (!crew.IsAlive(member)) continue;
            firstAlive ??= member;
            if (KnowsAll(member, refs)) return member;
        }
        return firstAlive;
    }

    private bool KnowsAll(CrewId member, List<(string Raw, FactId Fact)> refs)
    {
        foreach (var (_, fact) in refs)
        {
            if (!knownFacts.Knows(member, fact)) return false;
        }
        return true;
    }

    private List<FactId> CheckFactsKnown(List<(string Raw, FactId Fact)> refs, CrewId? speaker, List<AiRejection> rejections)
    {
        var facts = new List<FactId>(refs.Count);
        foreach (var (raw, fact) in refs)
        {
            if (speaker is not { } id || !knownFacts.Knows(id, fact))
                rejections.Add(new AiRejection(AiRejectionCodes.FactNotKnown, raw));
            else if (!facts.Contains(fact))
                facts.Add(fact);
        }
        return facts;
    }

    private void CheckText(string text, string field, List<FactId> factRefs, List<AiRejection> rejections)
    {
        foreach (var token in AiGuardrails.FactTokens(text))
        {
            var listed = IdParsing.TryCreate(token, v => new FactId(v), out var fact) && factRefs.Contains(fact);
            if (!listed) rejections.Add(new AiRejection(AiRejectionCodes.FactTokenNotInRefs, token));
        }
        if (AiGuardrails.HasMalformedFactToken(text)) rejections.Add(new AiRejection(AiRejectionCodes.MalformedFactToken, field));
        if (guardrails.FindNumberWithUnit(text) is { } number) rejections.Add(new AiRejection(AiRejectionCodes.NumberWithUnit, number));
        if (guardrails.FindForbiddenWord(text) is { } word) rejections.Add(new AiRejection(AiRejectionCodes.ForbiddenWord, word));
        if (guardrails.FindComponentId(text) is not null) rejections.Add(new AiRejection(AiRejectionCodes.ComponentIdInText, field));
    }

    private CommandRequest? BuildCommand(AiIntent intent, AiValidationContext context, CrewId? assignee, List<AiRejection> rejections)
    {
        if (string.IsNullOrEmpty(intent.ActionId))
        {
            rejections.Add(new AiRejection(AiRejectionCodes.MissingAction));
            return null;
        }
        if (FindAction(intent.ActionId) is not { } action)
        {
            rejections.Add(new AiRejection(AiRejectionCodes.UnknownAction, intent.ActionId));
            return null;
        }

        var phase = state.GetText(CommandValidator.PhasePath);
        if (!action.IsAllowedIn(phase)) rejections.Add(new AiRejection(AiRejectionCodes.ActionNotAllowedInPhase, phase));

        var raw = new Dictionary<string, CommandArg>(StringComparer.Ordinal);
        foreach (var param in intent.Params ?? [])
        {
            if (!raw.TryAdd(param.Name, CommandArg.Of(param.Value)))
            {
                rejections.Add(new AiRejection(AiRejectionCodes.InvalidParams, $"duplicate {param.Name}"));
                return null;
            }
        }

        var parsed = ActionArguments.Parse(action, raw);
        if (parsed.Arguments is not { } args)
        {
            rejections.Add(new AiRejection(AiRejectionCodes.InvalidParams, $"{parsed.ReasonCode} {parsed.Detail}"));
            return null;
        }

        return new CommandRequest(context.RequestId, context.Source, action.Id, action.UsesCrew ? assignee : null, args.Values, Confirmed: false);
    }

    private CatalogAction? FindAction(string actionId) =>
        IdParsing.TryCreate(actionId, v => new ActionId(v), out var id) && catalog.TryGet(id, out var action) ? action : null;
}
