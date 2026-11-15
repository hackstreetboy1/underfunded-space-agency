using System.Globalization;
using Usa.Game.Content;
using Usa.Game.Flow.Screens;
using Usa.Game.Services.Saves;

namespace Usa.Game.UI.Frontend.Model;

/// <summary>Composed lines of the menu screens (G6-01, G6-02, G6-03, G6-10) that the flow models leave to the view.</summary>
public static class MenuText
{
    /// <summary>"Out of Control · Normal · resumes at MET 06:58:12" under Continue.</summary>
    public static string ContinueLine(ResumeMetadata resume, string missionTitle, string difficultyLabel, FrontendText text)
    {
        ArgumentNullException.ThrowIfNull(resume);
        ArgumentNullException.ThrowIfNull(text);
        return text.Format("mainMenu.continueLine", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mission"] = missionTitle,
            ["difficulty"] = difficultyLabel,
            ["met"] = MetText.FromMilliseconds(resume.MetMs),
        });
    }

    /// <summary>"98 / 100" for a record; null when the difficulty was never finished.</summary>
    public static string? RecordScore(MissionBestRecord? record, FrontendText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (record is null) return null;
        return text.Format("missionSelect.scoreOutOf", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["points"] = DebriefPresentation.Points(record.BestScorePoints),
        });
    }

    /// <summary>"2 of 3 objectives completed" (flight objectives only; R is the return, not counted).</summary>
    public static string RecordObjectives(MissionDifficultyRecord record, int flightObjectiveCount, FrontendText text)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(text);
        return text.Format("missionSelect.objectivesCompleted", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["completed"] = Math.Min(record.ObjectivesCompleted, flightObjectiveCount).ToString(CultureInfo.InvariantCulture),
            ["total"] = flightObjectiveCount.ToString(CultureInfo.InvariantCulture),
        });
    }

    /// <summary>Flight objectives of a briefing (every objective except the R return objective).</summary>
    public static int FlightObjectiveCount(IReadOnlyList<BriefingObjective> objectives)
    {
        ArgumentNullException.ThrowIfNull(objectives);
        return objectives.Count(o => o.Id.Value != DebriefPresentation.ReturnObjectiveId);
    }

    /// <summary>"O1 · 30 %" from the objective id and the leading percentage of its weight note.</summary>
    public static string ObjectiveTag(BriefingObjective objective)
    {
        ArgumentNullException.ThrowIfNull(objective);
        var code = DebriefPresentation.CodeOf(objective.Id.Value);
        var note = objective.WeightNote;
        var percent = note.IndexOf('%', StringComparison.Ordinal);
        return percent > 0 && percent <= 5 ? $"{code} · {note[..(percent + 1)]}" : code;
    }

    /// <summary>"MISSION 1 · 1966 · EARTH ORBIT, ABOUT 300 KM".</summary>
    public static string CardEyebrow(int missionNumber, string era, string orbit, FrontendText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var mission = text.Format("missionSelect.missionNumber", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["number"] = missionNumber.ToString(CultureInfo.InvariantCulture),
        });
        return string.Join(" · ", new[] { mission, era, orbit }.Where(p => !string.IsNullOrWhiteSpace(p))).ToUpperInvariant();
    }

    /// <summary>"1966 · Earth orbit · Normal" under the loading title.</summary>
    public static string LoadingSubtitle(string era, string? setting, string difficultyLabel) =>
        string.Join(" · ", new[] { era, setting, difficultyLabel }.Where(p => !string.IsNullOrWhiteSpace(p)));

    /// <summary>The briefing header line, "United Space Administration · Gemini 8 … · starts at MET 06:20:00".</summary>
    public static string BriefingHeaderLine(BriefingHeader header, FrontendText text)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(text);
        return text.Format("briefing.headerLine", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["agency"] = header.Agency,
            ["vehicles"] = header.Vehicles,
            ["era"] = header.Era,
            ["orbit"] = header.Orbit,
            ["start"] = header.MissionStart,
        });
    }

    /// <summary>"Propulsion · 3 entries" over the encyclopedia list.</summary>
    public static string EncyclopediaListTitle(string categoryLabel, int count, FrontendText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Format("encyclopedia.listTitle", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["category"] = categoryLabel,
            ["count"] = count.ToString(CultureInfo.InvariantCulture),
        }).ToUpperInvariant();
    }
}
