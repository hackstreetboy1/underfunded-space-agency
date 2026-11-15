using System.Globalization;
using Usa.Game.Content;
using Usa.Simulation.Scoring;

namespace Usa.Game.UI.Frontend.Model;

/// <summary>Status tone of a debrief mark; the scene resolves it to a token colour and a shape (never colour alone).</summary>
public enum DebriefTone
{
    Ok,
    Caution,
    Critical,
    Neutral,
}

public sealed record DebriefDimensionRow(string Name, string PointsText, string MaxText, double Ratio, DebriefTone Tone);

/// <param name="Code">"O1", "O2", "R".</param>
/// <param name="Detail">The explanation line, with the cause and reason appended when the objective was lost.</param>
/// <param name="MetText">MET of the last feasibility change; null when none.</param>
/// <param name="LossNote">"Avoidable: …" or the scenario cause; null when not lost.</param>
public sealed record DebriefObjectiveRow(string Code, string Title, string StateText, DebriefTone Tone, string Detail, string? MetText, string? LossNote);

public sealed record DebriefCrewRow(string Name, string LaunchText, string WorstText, string RecoveryText, bool Lost);

/// <param name="KnownLine">"What you knew then: …"; null for an incident.</param>
public sealed record DebriefTimelineRow(string MetText, string KindText, DebriefTimelineKind Kind, string Text, string? KnownLine);

public sealed record DebriefDecisionRow(string Text, string? MetText);

/// <summary>
/// Everything the debrief scene shows (G6-04a/b), worded and toned from a composed <see cref="DebriefView"/>. Adds the R
/// row (Gate 6 decision 3, approved at its default): "Completed" when every astronaut came home, "1 of 2 crew home"
/// otherwise, "Not landed" when the capsule never landed. Hints never enter the debrief, so nothing here reads them.
/// </summary>
public sealed class DebriefPresentation
{
    public const string ReturnObjectiveId = "r_safe_return";
    public const double OkRatio = 0.75;
    public const double CautionRatio = 0.5;
    private const string NotLandedKey = "return.not_landed";

    private DebriefPresentation()
    {
    }

    public required string Title { get; init; }

    public required string Meta { get; init; }

    public required string GradeLabel { get; init; }

    public required string BandLine { get; init; }

    public required string FinalText { get; init; }

    public required string FinalSuffix { get; init; }

    public required string RawLine { get; init; }

    public string? CapNote { get; init; }

    /// <summary>Crew losses with their MET, then the splashdown line (<see cref="BuildFlightLines"/>).</summary>
    public required IReadOnlyList<string> FlightLines { get; init; }

    /// <summary>"Saved to your mission record." or the not-saved line; null when the caller did not say.</summary>
    public string? RecordLine { get; init; }

    public bool RecordSaved { get; init; }

    public required IReadOnlyList<DebriefDimensionRow> Dimensions { get; init; }

    public required IReadOnlyList<DebriefObjectiveRow> Objectives { get; init; }

    public required IReadOnlyList<DebriefCrewRow> Crew { get; init; }

    public required IReadOnlyList<DebriefTimelineRow> Timeline { get; init; }

    public required IReadOnlyList<DebriefDecisionRow> BestDecisions { get; init; }

    public DebriefDecisionRow? LargestAvoidableLoss { get; init; }

    public required string SPlusTitle { get; init; }

    public required bool SPlusAchieved { get; init; }

    public required IReadOnlyList<string> SPlusReasons { get; init; }

    /// <param name="missionTitle">Briefing title, e.g. "Out of Control".</param>
    /// <param name="difficultyLabel">"Normal".</param>
    /// <param name="objectives">Briefing objectives, for the R row's title.</param>
    /// <param name="saved">Whether the run reached the mission record (<c>ProfileRecorder.StatusOf</c>); null shows no record line.</param>
    public static DebriefPresentation Build(
        DebriefView view,
        string missionTitle,
        string difficultyLabel,
        ulong seed,
        IReadOnlyList<BriefingObjective> objectives,
        IReadOnlyList<GradeBand> bands,
        FrontendText text,
        bool? saved = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(missionTitle);
        ArgumentNullException.ThrowIfNull(difficultyLabel);
        ArgumentNullException.ThrowIfNull(objectives);
        ArgumentNullException.ThrowIfNull(bands);
        ArgumentNullException.ThrowIfNull(text);

        var rows = view.Objectives.Select(o => ObjectiveRow(o)).ToList();
        if (!view.Objectives.Any(o => o.Id.Value == ReturnObjectiveId)) rows.Add(ReturnRow(view, objectives, text));

        var known = view.Labels.KnownAtTheTime;
        return new DebriefPresentation
        {
            Title = text.Format("debrief.title", One("mission", missionTitle)),
            Meta = text.Format("debrief.meta", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["difficulty"] = difficultyLabel,
                ["seed"] = seed.ToString(CultureInfo.InvariantCulture),
            }),
            GradeLabel = view.GradeLabel,
            BandLine = GradeBandText.Format(view.Grade, view.GradeLabel, bands, text),
            FinalText = Points(view.FinalPoints),
            FinalSuffix = text.Get("debrief.finalSuffix"),
            RawLine = text.Format("debrief.raw", One("raw", Points(view.RawPoints))),
            CapNote = view.CapNote,
            Dimensions = view.Dimensions.Select(DimensionRow).ToArray(),
            Objectives = rows,
            Crew = view.CrewConditions
                .Select(c => new DebriefCrewRow(
                    c.Name, c.LaunchText, c.WorstText,
                    c.LostMetText is { } lostAt ? text.Format("debrief.crewLostAt", One("met", lostAt)) : c.RecoveryText,
                    c.RecoveryHealth is null))
                .ToArray(),
            FlightLines = BuildFlightLines(view, text),
            RecordLine = saved is { } wasSaved ? text.Get(wasSaved ? "debrief.recordSaved" : "debrief.recordNotSaved") : null,
            RecordSaved = saved ?? false,
            Timeline = view.Timeline.Select(t => TimelineRow(t, known, view.Labels.NothingKnown, text)).ToArray(),
            BestDecisions = view.BestDecisions.Select(d => new DebriefDecisionRow(d.Text, d.MetText)).ToArray(),
            LargestAvoidableLoss = view.LargestAvoidableLoss is { } loss ? new DebriefDecisionRow(loss.Text, loss.MetText) : null,
            SPlusTitle = view.SPlus.Title,
            SPlusAchieved = view.SPlus.Achieved,
            SPlusReasons = view.SPlus.Reasons.Select(r => r.Text).ToArray(),
        };
    }

    /// <summary>
    /// The header's flight summary from the debrief facts: one line per astronaut lost ("June Okafor lost · MET 07:06:12"), then
    /// the splashdown (MET, zone, inside or outside the recovery zones) or "No splashdown".
    /// </summary>
    public static IReadOnlyList<string> BuildFlightLines(DebriefView view, FrontendText text)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(text);
        var lines = view.CrewLosses
            .Select(loss => text.Format("debrief.crewLostLine", ("name", loss.Name), ("met", loss.MetText)))
            .ToList();
        lines.Add(view.Splashdown is { } splashdown
            ? text.Format(splashdown.InZone ? "debrief.splashdownInZone" : "debrief.splashdownOutside", ("met", splashdown.MetText), ("zone", splashdown.ZoneName))
            : text.Get("debrief.noSplashdown"));
        return lines;
    }

    /// <summary>Bar tone from the share of points (G6-04: colour from the ratio, points always shown).</summary>
    public static DebriefTone ToneOf(double ratio) =>
        !double.IsFinite(ratio) ? DebriefTone.Neutral
        : ratio >= OkRatio ? DebriefTone.Ok
        : ratio >= CautionRatio ? DebriefTone.Caution
        : DebriefTone.Critical;

    /// <summary>"o1_dock" → "O1", "r_safe_return" → "R"; other ids keep their first segment upper-cased.</summary>
    public static string CodeOf(string objectiveId)
    {
        ArgumentNullException.ThrowIfNull(objectiveId);
        var underscore = objectiveId.IndexOf('_', StringComparison.Ordinal);
        var head = underscore > 0 ? objectiveId[..underscore] : objectiveId;
        return head.ToUpperInvariant();
    }

    public static string Points(double points) =>
        double.IsFinite(points) ? Math.Round(points, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) : "–";

    private static DebriefDimensionRow DimensionRow(DebriefDimensionView dimension)
    {
        var ratio = dimension.MaxPoints > 0 ? Math.Clamp(dimension.Points / dimension.MaxPoints, 0, 1) : 0;
        return new DebriefDimensionRow(dimension.Name, Points(dimension.Points), Points(dimension.MaxPoints), ratio, ToneOf(ratio));
    }

    private static DebriefObjectiveRow ObjectiveRow(DebriefObjectiveView objective)
    {
        var tone = objective.State switch
        {
            ObjectiveFinalState.Completed => DebriefTone.Ok,
            _ when objective.Cause == ObjectiveLossCause.PlayerAvoidable => DebriefTone.Critical,
            ObjectiveFinalState.Abandoned => DebriefTone.Neutral,
            _ => DebriefTone.Caution,
        };
        string? lossNote = null;
        if (objective.State != ObjectiveFinalState.Completed)
        {
            var parts = new[] { objective.CauseText, objective.ReasonText }.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
            lossNote = parts.Length == 0 ? null : string.Join(" ", parts);
        }
        return new DebriefObjectiveRow(
            CodeOf(objective.Id.Value), objective.Title, objective.StateText, tone, objective.Explanation,
            objective.State == ObjectiveFinalState.Completed ? null : objective.FinalMetText, lossNote);
    }

    private static DebriefObjectiveRow ReturnRow(DebriefView view, IReadOnlyList<BriefingObjective> objectives, FrontendText text)
    {
        var title = objectives.FirstOrDefault(o => o.Id.Value == ReturnObjectiveId)?.Title ?? ReturnObjectiveId;
        var safeReturn = view.Dimensions.FirstOrDefault(d => d.Dimension == ScoreDimension.SafeReturn);
        var notLanded = safeReturn is not null
                        && (safeReturn.Lines.Any(l => l.Key == NotLandedKey) || safeReturn.Notes.Any(n => n.Key == NotLandedKey));
        var detail = safeReturn?.Lines.FirstOrDefault(l => l.Key.StartsWith("return.recovery_zone", StringComparison.Ordinal))?.Text
                     ?? (safeReturn is { Lines.Count: > 0 } ? safeReturn.Lines[0].Text : null)
                     ?? "";

        if (notLanded) return new DebriefObjectiveRow("R", title, text.Get("debrief.returnNotLanded"), DebriefTone.Critical, detail, null, null);

        var crew = view.CrewConditions;
        var home = crew.Count(c => c.RecoveryHealth is not null);
        if (crew.Count == 0 || home == crew.Count)
            return new DebriefObjectiveRow("R", title, text.Get("debrief.returnCompleted"), DebriefTone.Ok, detail, null, null);

        var state = text.Format("debrief.returnCrewHome", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["home"] = home.ToString(CultureInfo.InvariantCulture),
            ["count"] = crew.Count.ToString(CultureInfo.InvariantCulture),
        });
        return new DebriefObjectiveRow("R", title, state, home == 0 ? DebriefTone.Critical : DebriefTone.Caution, detail, null, null);
    }

    private static DebriefTimelineRow TimelineRow(DebriefTimelineEntryView entry, string knownLabel, string nothingKnown, FrontendText text)
    {
        string? known = null;
        if (entry.Kind != DebriefTimelineKind.Incident)
        {
            var items = entry.KnownAtTheTime.Count == 0 ? nothingKnown : string.Join("; ", entry.KnownAtTheTime);
            known = text.Format("debrief.knownLine", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["label"] = knownLabel,
                ["items"] = items,
            });
        }
        return new DebriefTimelineRow(entry.MetText, entry.KindText, entry.Kind, entry.Text, known);
    }

    private static Dictionary<string, string> One(string name, string value) => new(StringComparer.Ordinal) { [name] = value };
}
