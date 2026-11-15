namespace Usa.Game.Presentation.EntryHud;

/// <summary>
/// Player-facing words of the entry HUD and splashdown card that come from <c>data/content/strings.json</c>
/// (<see cref="Keys"/>). The models stay free of the text store: the host passes <see cref="From"/> over its strings.
/// <see cref="None"/> leaves those words empty, so the views hide them rather than show untranslated text.
/// Templates use <c>{name}</c> placeholders.
/// </summary>
public sealed record EntryHudWords(
    string PeakG,
    string CorridorLine,
    string CorridorOk,
    string CorridorTooSteep,
    string CorridorTooShallow,
    string CorridorNearEdge,
    string InZone,
    string OutsideZones,
    string StepEstimated,
    string SplashPointTitle,
    string SplashPointEast,
    string SplashPointWest,
    string SplashAt)
{
    /// <summary>The string keys, in constructor order.</summary>
    public static IReadOnlyList<string> Keys { get; } =
    [
        "map.entryPeakG", "map.entryCorridorLine", "map.corridorOk", "map.corridorTooSteep", "map.corridorTooShallow",
        "map.zoneNearEdge", "map.zoneInZone", "map.outsideZones", "map.entryStepEstimated", "map.splashPointTitle",
        "map.splashPointEast", "map.splashPointWest", "map.splashAt", "map.awaitingRecoveryReport", "map.awaitingRecoveryReportBadge",
    ];

    public static EntryHudWords None { get; } = new("", "", "", "", "", "", "", "", "", "", "", "", "");

    /// <summary>The splashdown card's zone line before the recovery forces report (<c>Awaiting recovery report</c>).</summary>
    public string AwaitingRecoveryReport { get; init; } = "";

    /// <summary>Its badge (<c>AWAITING REPORT</c>).</summary>
    public string AwaitingRecoveryReportBadge { get; init; } = "";

    /// <param name="get">Looks a key of <see cref="Keys"/> up, e.g. <c>key =&gt; strings.Get(key)</c>.</param>
    public static EntryHudWords From(Func<string, string> get)
    {
        ArgumentNullException.ThrowIfNull(get);
        var words = new string[Keys.Count];
        for (var i = 0; i < words.Length; i++) words[i] = get(Keys[i]) ?? "";
        return new EntryHudWords(
            words[0], words[1], words[2], words[3], words[4], words[5], words[6], words[7], words[8], words[9], words[10], words[11], words[12])
        {
            AwaitingRecoveryReport = words[13],
            AwaitingRecoveryReportBadge = words[14],
        };
    }

    /// <summary>Fills <c>{name}</c> placeholders; an empty template stays empty.</summary>
    public static string Fill(string template, params (string Name, string Value)[] values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);
        if (template.Length == 0) return "";
        var text = template;
        foreach (var (name, value) in values) text = text.Replace("{" + name + "}", value, StringComparison.Ordinal);
        return text;
    }
}
