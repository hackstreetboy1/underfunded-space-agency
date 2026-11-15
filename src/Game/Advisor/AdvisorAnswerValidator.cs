using System.Text.RegularExpressions;
using Usa.Game.Services.Ai.Contracts;
using Usa.Simulation.Commands;

namespace Usa.Game.Advisor;

/// <summary>
/// Checks an explain answer before the player sees it (GDD 9.2): non-empty and short, sources limited to the excerpts
/// that were sent, no forbidden words or raw component ids (<c>ai_guardrails.json</c>), no recommendation or imperative
/// fix (<c>advisor_guardrails.json</c>), no markup or fact tokens, no number with a unit unless an excerpt states it, and no
/// call-sign opener ("CAPCOM, ..."): the answer is reference text, not a radio call.
/// Collects every problem.
/// </summary>
public sealed partial class AdvisorAnswerValidator(AiGuardrails aiGuardrails, AdvisorGuardrails advisorGuardrails)
{
    public IReadOnlyList<AiRejection> Validate(ExplainResponse response, IReadOnlyList<EncyclopediaExcerpt> excerpts)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(excerpts);
        var rejections = new List<AiRejection>();
        var answer = response.Answer?.Trim() ?? "";

        if (answer.Length == 0) rejections.Add(new AiRejection(AdvisorRejectionCodes.EmptyAnswer));
        else if (answer.Length > advisorGuardrails.MaxAnswerChars) rejections.Add(new AiRejection(AdvisorRejectionCodes.AnswerTooLong));

        foreach (var source in response.Sources ?? [])
        {
            if (!excerpts.Any(e => string.Equals(e.Id, source, StringComparison.Ordinal)))
                rejections.Add(new AiRejection(AdvisorRejectionCodes.UnknownSource, source));
        }

        if (aiGuardrails.FindForbiddenWord(answer) is { } word) rejections.Add(new AiRejection(AiRejectionCodes.ForbiddenWord, word));
        if (aiGuardrails.FindComponentId(answer) is { } prefix) rejections.Add(new AiRejection(AiRejectionCodes.ComponentIdInText, prefix));
        if (FindMarkup(answer) is { } markup) rejections.Add(new AiRejection(AdvisorRejectionCodes.Markup, markup));
        if (advisorGuardrails.FindRecommendation(answer) is { } phrase) rejections.Add(new AiRejection(AdvisorRejectionCodes.Recommendation, phrase));
        if (FindCallSignOpener(answer) is { } sign) rejections.Add(new AiRejection(AdvisorRejectionCodes.CallSignOpener, sign));

        var allowed = aiGuardrails.FindNumbersWithUnits(string.Join('\n', excerpts.Select(e => $"{e.Title}\n{e.Text}")))
            .Select(Normalize).ToHashSet(StringComparer.Ordinal);
        foreach (var number in aiGuardrails.FindNumbersWithUnits(answer))
        {
            if (!allowed.Contains(Normalize(number))) rejections.Add(new AiRejection(AdvisorRejectionCodes.NumberNotInExcerpts, number));
        }
        return rejections;
    }

    /// <summary>
    /// Answers are plain text: an angle bracket (HTML, <c>&lt;FACT:…&gt;</c> tokens, which explain has no facts for) or a
    /// BBCode tag would reach the sheet raw or be interpreted by a rich-text label.
    /// </summary>
    private static string? FindMarkup(string answer)
    {
        var normalized = AiGuardrails.Normalize(answer);
        var match = Markup().Match(normalized);
        return match.Success ? match.Value : null;
    }

    /// <summary>The ground's call sign as the first words ("CAPCOM, rate damping ..."), a radio habit the prompt forbids.</summary>
    private static string? FindCallSignOpener(string answer)
    {
        var match = CallSignOpener().Match(AiGuardrails.Normalize(answer));
        return match.Success ? match.Groups["sign"].Value : null;
    }

    /// <summary>True when the call-sign opener is the answer's only fault, so <see cref="WithoutCallSignOpener"/> may save it.</summary>
    public static bool OnlyCallSignOpener(IReadOnlyList<AiRejection> rejections)
    {
        ArgumentNullException.ThrowIfNull(rejections);
        return rejections.Count > 0 && rejections.All(r => r.Code == AdvisorRejectionCodes.CallSignOpener);
    }

    /// <summary>
    /// <paramref name="answer"/> without its call-sign opener ("Ground, rate damping uses ..." → "Rate damping uses ...");
    /// null when it has none. The result still needs <see cref="Validate"/>.
    /// </summary>
    public static string? WithoutCallSignOpener(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        // Match the text as written so the sheet keeps its characters; a zero-width trick shows only once normalized.
        var match = CallSignOpener().Match(answer);
        if (!match.Success)
        {
            answer = AiGuardrails.Normalize(answer);
            match = CallSignOpener().Match(answer);
            if (!match.Success) return null;
        }
        return Rejoin(match.Groups["lead"].Value, answer[match.Length..]);
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

    /// <summary>"7.8 KM" and "7.8km" are the same figure.</summary>
    private static string Normalize(string numberWithUnit) =>
        string.Concat(numberWithUnit.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    [GeneratedRegex(@"[<>]|\[/?[A-Za-z]+(?:=[^\]]*)?\]", RegexOptions.CultureInvariant)]
    private static partial Regex Markup();

    [GeneratedRegex(@"\A(?<lead>\W*)(?:(?:hey|hi|hello|okay|ok|so|well)\W+)?(?<sign>capcom|ground)\s*[,:;!.—–-]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CallSignOpener();
}
