using Usa.Game.Content;

namespace Usa.Game.Flow.Data;

/// <param name="Era">Year shown on the card, e.g. "1966".</param>
/// <param name="Setting">Where the mission flies, e.g. "Earth orbit"; null on locked cards (title and era only, UI.md 5).</param>
public sealed record MissionCatalogEntry(string MissionId, string Title, string Era, string? Setting, bool Locked);

/// <summary>The missions on mission select, in campaign order (GDD 11).</summary>
public sealed class MissionCatalog
{
    public MissionCatalog(IReadOnlyList<MissionCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = entries;
    }

    public IReadOnlyList<MissionCatalogEntry> Entries { get; }

    /// <summary>
    /// The vertical slice: mission 1 from its briefing plus the titled placeholders of the 3-mission campaign
    /// (GDD 11), whose titles and eras are <c>missionSelect.*</c> strings until those missions exist.
    /// </summary>
    public static MissionCatalog ForSlice(BriefingDefinition missionOne, ITextSource text)
    {
        ArgumentNullException.ThrowIfNull(missionOne);
        ArgumentNullException.ThrowIfNull(text);
        return new MissionCatalog(
        [
            new MissionCatalogEntry(missionOne.MissionId, missionOne.Title, missionOne.Header.Era, SettingOf(missionOne.Header.Orbit), Locked: false),
            new MissionCatalogEntry(
                "a_roof_gone_missing", text.Get("missionSelect.roofGoneMissingTitle"), text.Get("missionSelect.roofGoneMissingEra"),
                Setting: null, Locked: true),
            new MissionCatalogEntry(
                "the_quiet_tank", text.Get("missionSelect.quietTankTitle"), text.Get("missionSelect.quietTankEra"), Setting: null, Locked: true),
        ]);
    }

    public MissionCatalogEntry? Find(string missionId) =>
        Entries.FirstOrDefault(e => string.Equals(e.MissionId, missionId, StringComparison.Ordinal));

    /// <summary>The card shows the setting ("Earth orbit"); the briefing header has the detail ("Earth orbit, about 300 km").</summary>
    private static string SettingOf(string orbit)
    {
        var comma = orbit.IndexOf(',', StringComparison.Ordinal);
        return (comma > 0 ? orbit[..comma] : orbit).Trim();
    }
}
