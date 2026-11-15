namespace Usa.Game.Presentation.EntryHud;

/// <summary>
/// The splashdown / recovery card (UI 4.3): landing zone, distance to the recovery ship, estimated recovery wait with
/// its progress (the small ship/helicopter icon), crew status, and "Proceed to debrief", enabled once the crew is
/// recovered. While waiting, <see cref="CompressTime"/> asks the host to time-compress the wait automatically.
/// </summary>
/// <param name="Visible">The capsule has splashed down.</param>
/// <param name="WaitEstimateText">Total estimate, <c>≈ 3 h</c>.</param>
/// <param name="WaitRemainingText"><c>in 2 h 10 min</c>, <c>due now</c> or <c>Recovered</c>.</param>
/// <param name="RecoveryProgress01">Position of the recovery icon along its track.</param>
/// <param name="InZone">The reported or estimated zone verdict (never truth); false while <paramref name="AwaitingReport"/>.</param>
/// <param name="ZoneBadgeText"><c>IN ZONE</c> or <c>OUTSIDE ZONES</c>; empty without words.</param>
/// <param name="SplashPointText">e.g. <c>135° E</c> as reported; empty when not reported.</param>
/// <param name="SplashMetText">e.g. <c>splashdown MET 08:25:38</c> as reported; empty when not reported.</param>
/// <param name="AwaitingReport">
/// Splashed down but the recovery forces have not reported yet (no zone and no verdict): the zone line and badge say so instead
/// of claiming the capsule is outside the zones.
/// </param>
public sealed record SplashdownCardModel(
    bool Visible,
    string ZoneText,
    string DistanceText,
    string WaitEstimateText,
    string WaitRemainingText,
    double RecoveryProgress01,
    IReadOnlyList<CrewMini> Crew,
    string CrewSummary,
    bool ProceedEnabled,
    bool CompressTime,
    bool InZone = false,
    string ZoneBadgeText = "",
    string SplashPointTitle = "",
    string SplashPointText = "",
    string SplashMetText = "",
    bool AwaitingReport = false)
{
    public const string ProceedLabel = "Proceed to debrief";
    public const string OutsideZonesText = "Outside recovery zones";
    public const string AwaitingReportText = "Awaiting recovery report";
    public const string AwaitingReportBadge = "AWAITING REPORT";
    public const string RecoveredText = "Recovered";
    public const string DueText = "due now";

    // Cached: Enum.GetValues allocates a new array on every call, and the card is rebuilt at 10 Hz.
    private static readonly int BandCount = Enum.GetValues<CrewHealthBand>().Length;

    /// <param name="words">Strings for the zone badge and splash point (<see cref="EntryHudWords.From"/>); null leaves them empty.</param>
    public static SplashdownCardModel Build(IEntryHudSource source, EntryHudWords? words = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Build(source.Splashdown, source.Crew, words);
    }

    public static SplashdownCardModel Build(SplashdownInputs splashdown, IReadOnlyList<EntryCrewInputs> crew, EntryHudWords? words = null)
    {
        ArgumentNullException.ThrowIfNull(splashdown);
        ArgumentNullException.ThrowIfNull(crew);
        words ??= EntryHudWords.None;

        var recovered = splashdown.SplashedDown && splashdown.Recovered;
        var total = splashdown.RecoveryWaitSec is { } wait && double.IsFinite(wait) && wait >= 0 ? wait : (double?)null;
        var elapsed = double.IsFinite(splashdown.RecoveryElapsedSec) ? Math.Max(0, splashdown.RecoveryElapsedSec) : 0;

        double progress;
        string remaining;
        if (recovered)
        {
            progress = 1;
            remaining = RecoveredText;
        }
        else if (total is { } t)
        {
            progress = t > 0 ? Math.Clamp(elapsed / t, 0, 1) : 1;
            remaining = elapsed >= t ? DueText : "in " + EntryHudFormat.Span(t - elapsed);
        }
        else
        {
            progress = 0;
            remaining = EntryHudFormat.Placeholder;
        }

        var awaiting = splashdown.SplashedDown && splashdown.InZone is null && string.IsNullOrWhiteSpace(splashdown.ZoneName);
        var inZone = splashdown.InZone ?? !string.IsNullOrWhiteSpace(splashdown.ZoneName);
        var splashPoint = "";
        if (splashdown.SplashLongitudeDeg is { } longitude && double.IsFinite(longitude))
        {
            var (degrees, east) = EntryHudFormat.Longitude(longitude);
            splashPoint = EntryHudWords.Fill(east ? words.SplashPointEast : words.SplashPointWest, ("longitude", degrees));
        }
        var splashMet = splashdown.SplashMetS is { } met && double.IsFinite(met)
            ? EntryHudWords.Fill(words.SplashAt, ("time", EntryHudFormat.Clock(met)))
            : "";

        var minis = EntryHudModel.CrewMinis(crew);
        return new SplashdownCardModel(
            splashdown.SplashedDown,
            awaiting ? Word(words.AwaitingRecoveryReport, AwaitingReportText)
            : string.IsNullOrWhiteSpace(splashdown.ZoneName) ? OutsideZonesText
            : splashdown.ZoneName,
            splashdown.DistanceToShipKm is { } km ? EntryHudFormat.DistanceKm(km) : EntryHudFormat.Placeholder,
            total is { } estimate ? EntryHudFormat.WaitEstimate(estimate) : EntryHudFormat.Placeholder,
            remaining,
            progress,
            minis,
            Summarize(minis),
            ProceedEnabled: recovered,
            CompressTime: splashdown.SplashedDown && !recovered,
            InZone: inZone,
            ZoneBadgeText: awaiting ? Word(words.AwaitingRecoveryReportBadge, AwaitingReportBadge) : inZone ? words.InZone : words.OutsideZones,
            SplashPointTitle: splashPoint.Length > 0 ? words.SplashPointTitle : "",
            SplashPointText: splashPoint,
            SplashMetText: splashMet,
            AwaitingReport: awaiting);
    }

    private static string Word(string word, string fallback) => word.Length > 0 ? word : fallback;

    /// <summary><c>All crew stable</c>, or counts per band worst last: <c>1 stable · 1 impaired</c>.</summary>
    public static string Summarize(IReadOnlyList<CrewMini> crew)
    {
        ArgumentNullException.ThrowIfNull(crew);
        if (crew.Count == 0) return EntryHudFormat.Placeholder;

        var counts = new int[BandCount];
        foreach (var member in crew) counts[(int)member.Band]++;
        if (counts[(int)CrewHealthBand.Stable] == crew.Count) return "All crew stable";

        var parts = new List<string>();
        foreach (var band in SummaryOrder)
        {
            var count = counts[(int)band];
            if (count > 0) parts.Add(FormattableString.Invariant($"{count} {LowerWord(band)}"));
        }
        return string.Join(SequenceStep.Separator, parts);
    }

    private static string LowerWord(CrewHealthBand band) => band switch
    {
        CrewHealthBand.Stable => "stable",
        CrewHealthBand.Stressed => "stressed",
        CrewHealthBand.Impaired => "impaired",
        CrewHealthBand.Critical => "critical",
        CrewHealthBand.Deceased => "deceased",
        _ => "unknown",
    };

    private static readonly CrewHealthBand[] SummaryOrder =
    [
        CrewHealthBand.Stable, CrewHealthBand.Stressed, CrewHealthBand.Impaired, CrewHealthBand.Critical,
        CrewHealthBand.Deceased, CrewHealthBand.Unknown,
    ];
}
