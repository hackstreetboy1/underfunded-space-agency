using System.Text.RegularExpressions;
using Usa.Game.Services.Ai.Contracts;
using Usa.Simulation.Commands;
using Usa.Simulation.Guidance;

namespace Usa.Game.Guidance;

/// <summary>
/// Checks a <c>/v1/tip</c> rephrasing against the authored hint it was asked to rephrase (GDD 9.1): same tip and
/// stage; fact and action refs within the authored ones; non-empty and within the hint length limit; no forbidden
/// words, raw component ids or new fact tokens (<c>ai_guardrails.json</c>); no number with a unit the authored text
/// does not state; at every stage, no action or equipment designator ("T8", "ring B") the authored text does not name; no
/// call-sign opener ("CAPCOM, ...") unless the authored text has one: the card is a written note, not a radio call.
/// Collects every problem; any rejection keeps the authored text.
/// </summary>
public sealed partial class TipRephraseValidator
{
    private readonly AiGuardrails _guardrails;
    private readonly TipActionVerbs _actions;
    private readonly int _maxTextLength;

    /// <param name="maxTextLength">Defaults to the authored limit (<see cref="HintTreeLoader.MaxTextLength"/>).</param>
    public TipRephraseValidator(AiGuardrails guardrails, TipActionVerbs? actions = null, int maxTextLength = HintTreeLoader.MaxTextLength)
    {
        ArgumentNullException.ThrowIfNull(guardrails);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTextLength, 1);
        _guardrails = guardrails;
        _actions = actions ?? TipActionVerbs.Default;
        _maxTextLength = maxTextLength;
    }

    public IReadOnlyList<AiRejection> Validate(TipResponse response, HintCandidate authored)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(authored);
        var rejections = new List<AiRejection>();

        if (!string.Equals(response.TipId, authored.TipId, StringComparison.Ordinal))
            rejections.Add(new AiRejection(TipRejectionCodes.TipMismatch, response.TipId));
        if (response.Stage != authored.Stage) rejections.Add(new AiRejection(TipRejectionCodes.StageMismatch, response.Stage.ToString()));
        CheckSubset(response.FactRefs, authored.FactRefs, TipRejectionCodes.FactNotAllowed, rejections);
        CheckSubset(response.AllowedActionRefs, authored.AllowedActionRefs, TipRejectionCodes.ActionNotAllowed, rejections);

        var text = response.Text?.Trim() ?? "";
        if (text.Length == 0)
        {
            rejections.Add(new AiRejection(TipRejectionCodes.EmptyText));
            return rejections;
        }
        if (text.Length > _maxTextLength) rejections.Add(new AiRejection(TipRejectionCodes.TextTooLong, text.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        if (_guardrails.FindForbiddenWord(text) is { } word) rejections.Add(new AiRejection(AiRejectionCodes.ForbiddenWord, word));
        if (_guardrails.FindComponentId(text) is { } prefix) rejections.Add(new AiRejection(AiRejectionCodes.ComponentIdInText, prefix));
        CheckFactTokens(text, authored.Text, rejections);
        CheckNumbers(text, authored.Text, rejections);
        // Recommend and Direct are checked too: a stronger hint must not grow new instructions.
        CheckActions(text, authored.Text, rejections);
        CheckDesignators(text, authored.Text, rejections);
        CheckOpener(text, authored.Text, rejections);
        return rejections;
    }

    private static void CheckSubset(IReadOnlyList<string>? refs, IReadOnlyList<string> allowed, string code, List<AiRejection> rejections)
    {
        foreach (var id in refs ?? [])
        {
            if (!allowed.Contains(id, StringComparer.Ordinal)) rejections.Add(new AiRejection(code, id));
        }
    }

    /// <summary>Authored tips carry no fact tokens today; a rephrasing may keep the authored ones and add none.</summary>
    private static void CheckFactTokens(string text, string authoredText, List<AiRejection> rejections)
    {
        if (AiGuardrails.HasMalformedFactToken(text)) rejections.Add(new AiRejection(AiRejectionCodes.MalformedFactToken, "text"));
        var authoredTokens = AiGuardrails.FactTokens(authoredText);
        foreach (var token in AiGuardrails.FactTokens(text))
        {
            if (!authoredTokens.Contains(token, StringComparer.Ordinal)) rejections.Add(new AiRejection(AiRejectionCodes.FactTokenNotInRefs, token));
        }
    }

    private void CheckNumbers(string text, string authoredText, List<AiRejection> rejections)
    {
        var allowed = _guardrails.FindNumbersWithUnits(authoredText).Select(Normalize).ToHashSet(StringComparer.Ordinal);
        foreach (var number in _guardrails.FindNumbersWithUnits(text))
        {
            if (!allowed.Contains(Normalize(number))) rejections.Add(new AiRejection(AiRejectionCodes.NumberWithUnit, number));
        }
    }

    private void CheckActions(string text, string authoredText, List<AiRejection> rejections)
    {
        var authored = _actions.Find(authoredText);
        foreach (var action in _actions.Find(text).Order(StringComparer.Ordinal))
        {
            if (!_actions.IsCovered(action, authored)) rejections.Add(new AiRejection(TipRejectionCodes.NewAction, action));
        }
    }

    private static void CheckDesignators(string text, string authoredText, List<AiRejection> rejections)
    {
        var authored = TipDesignators.Find(authoredText);
        foreach (var designator in TipDesignators.Find(text))
        {
            if (!authored.Contains(designator)) rejections.Add(new AiRejection(TipRejectionCodes.NewDesignator, designator));
        }
    }

    /// <summary>The ground's call sign as the first words of the card ("CAPCOM, check ..."): a radio habit the prompt forbids.</summary>
    private static void CheckOpener(string text, string authoredText, List<AiRejection> rejections)
    {
        if (CallSignOpener().Match(AiGuardrails.Normalize(text)) is not { Success: true } match) return;
        if (CallSignOpener().IsMatch(AiGuardrails.Normalize(authoredText))) return;
        rejections.Add(new AiRejection(TipRejectionCodes.CallSignOpener, match.Groups["sign"].Value));
    }

    /// <summary>True when the call-sign opener is the rephrasing's only fault, so <see cref="WithoutCallSignOpener"/> may save it.</summary>
    public static bool OnlyCallSignOpener(IReadOnlyList<AiRejection> rejections)
    {
        ArgumentNullException.ThrowIfNull(rejections);
        return rejections.Count > 0 && rejections.All(r => r.Code == TipRejectionCodes.CallSignOpener);
    }

    /// <summary>
    /// <paramref name="text"/> without its call-sign opener ("CAPCOM, check the breakers" → "Check the breakers"); null
    /// when it has none. The result still needs <see cref="Validate"/>.
    /// </summary>
    public static string? WithoutCallSignOpener(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        // Match the text as written so the card keeps its characters; a zero-width trick shows only once normalized.
        var match = CallSignOpener().Match(text);
        if (!match.Success)
        {
            text = AiGuardrails.Normalize(text);
            match = CallSignOpener().Match(text);
            if (!match.Success) return null;
        }
        return Rejoin(match.Groups["lead"].Value, text[match.Length..]);
    }

    /// <summary>The rest of the text, capitalised, behind the opening quote the opener sat in (so a closing quote stays paired).</summary>
    private static string Rejoin(string lead, string rest)
    {
        rest = rest.TrimStart();
        if (rest.Length == 0) return rest;
        lead = lead.Trim();
        var quote = lead.Length > 0 && lead.All(c => c is '"' or '\'' or '“' or '‘' or '«') ? lead : "";
        return $"{quote}{char.ToUpperInvariant(rest[0])}{rest.AsSpan(1)}";
    }

    /// <summary>"2 m" and "2M" are the same figure.</summary>
    private static string Normalize(string numberWithUnit) =>
        string.Concat(numberWithUnit.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    [GeneratedRegex(@"\A(?<lead>\W*)(?:(?:hey|hi|hello|okay|ok|so|well)\W+)?(?<sign>capcom|ground)\s*[,:;!.—–-]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CallSignOpener();
}
