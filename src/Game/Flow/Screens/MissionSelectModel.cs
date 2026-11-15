using Usa.Game.Content;
using Usa.Game.Flow.Data;
using Usa.Game.Flow.Profile;
using Usa.Game.Services.Saves;

namespace Usa.Game.Flow.Screens;

/// <param name="BestGradeLabel">e.g. "B+"; null when this difficulty was never finished.</param>
/// <param name="BestGradeText">The label, or "Not flown yet".</param>
public sealed record MissionDifficultyRecord(
    string DifficultyId,
    string DifficultyLabel,
    string? BestGradeLabel,
    string BestGradeText,
    bool SPlusAchieved,
    int ObjectivesCompleted);

/// <param name="Setting">Null on locked cards: they show title and era only.</param>
/// <param name="Records">Best grade per playable difficulty; empty on locked cards.</param>
/// <param name="SPlusRibbon">S+ was achieved on any difficulty.</param>
public sealed record MissionCard(
    string MissionId,
    string Title,
    string Era,
    string? Setting,
    bool Locked,
    string? LockedLabel,
    IReadOnlyList<MissionDifficultyRecord> Records,
    bool SPlusRibbon);

/// <param name="LockedNote">Why it can't be picked ("Hard: arrives with the campaign").</param>
public sealed record DifficultyOption(string Id, string Label, bool Enabled, string? LockedNote);

/// <summary>
/// Mission select (UI.md 5, GDD 11): mission cards with records from the profile, locked placeholders for the
/// campaign, and the difficulty selector (Easy / Normal; Hard shown locked). Confirming hands the router a
/// selection whose seed is fixed per (mission, difficulty).
/// </summary>
public sealed class MissionSelectModel
{
    private readonly ScreenRouter _router;

    public MissionSelectModel(
        ScreenRouter router,
        MissionCatalog catalog,
        PlayerProfile profile,
        IReadOnlyList<DifficultyInfo> difficulties,
        StringTable strings)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(difficulties);
        ArgumentNullException.ThrowIfNull(strings);
        _router = router;

        Title = strings.Get("missionSelect.title");
        DifficultyTitle = strings.Get("missionSelect.difficulty");
        BestGradeTitle = strings.Get("missionSelect.bestGrade");
        Difficulties = BuildDifficulties(difficulties, strings);
        Cards = catalog.Entries.Select(e => BuildCard(e, profile, Difficulties, strings)).ToArray();

        SelectedMissionId = Cards.FirstOrDefault(c => !c.Locked)?.MissionId;
        SelectedDifficulty = Difficulties.FirstOrDefault(d => d.Enabled && d.Id == DifficultyInfo.Normal)?.Id
                             ?? Difficulties.FirstOrDefault(d => d.Enabled)?.Id;
    }

    public event Action? Changed;

    public string Title { get; }

    public string DifficultyTitle { get; }

    public string BestGradeTitle { get; }

    public IReadOnlyList<MissionCard> Cards { get; }

    public IReadOnlyList<DifficultyOption> Difficulties { get; }

    public string? SelectedMissionId { get; private set; }

    public string? SelectedDifficulty { get; private set; }

    public bool CanConfirm => SelectedMissionId is not null && SelectedDifficulty is not null;

    /// <summary>The seed the selected mission and difficulty will run with; Replay keeps it.</summary>
    public ulong? SelectedSeed => CanConfirm ? SeedPlanner.Initial(SelectedMissionId!, SelectedDifficulty!) : null;

    /// <summary>False for locked or unknown missions.</summary>
    public bool SelectMission(string missionId)
    {
        var card = Cards.FirstOrDefault(c => c.MissionId == missionId);
        if (card is null || card.Locked) return false;
        SelectedMissionId = missionId;
        Changed?.Invoke();
        return true;
    }

    /// <summary>False for locked (Hard) or unknown difficulties.</summary>
    public bool SelectDifficulty(string difficultyId)
    {
        var option = Difficulties.FirstOrDefault(d => d.Id == difficultyId);
        if (option is null || !option.Enabled) return false;
        SelectedDifficulty = difficultyId;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Opens the briefing for the selection.</summary>
    public bool Confirm() =>
        CanConfirm && _router.SelectMission(new MissionSelection(SelectedMissionId!, SelectedDifficulty!, SelectedSeed!.Value));

    public bool Back() => _router.Back();

    private static DifficultyOption[] BuildDifficulties(IReadOnlyList<DifficultyInfo> loaded, StringTable strings)
    {
        var options = new List<DifficultyOption>();
        foreach (var id in DifficultyInfo.SelectorOrder)
        {
            if (id == DifficultyInfo.Hard)
            {
                // D22: Hard arrives with the 3-mission campaign; shown so players know it exists.
                options.Add(new DifficultyOption(id, strings.Get("missionSelect.hard"), false, strings.Get("missionSelect.hardLocked")));
                continue;
            }

            var info = loaded.FirstOrDefault(d => d.Id == id);
            if (info is null || !info.Selectable) continue;
            var label = strings.TryGet($"missionSelect.{id}", out var text) ? text : info.DisplayName;
            options.Add(new DifficultyOption(id, label, true, null));
        }
        return [.. options];
    }

    private static MissionCard BuildCard(MissionCatalogEntry entry, PlayerProfile profile, IReadOnlyList<DifficultyOption> difficulties, StringTable strings)
    {
        if (entry.Locked)
            return new MissionCard(entry.MissionId, entry.Title, entry.Era, null, true, strings.Get("missionSelect.locked"), [], false);

        var records = new List<MissionDifficultyRecord>();
        foreach (var difficulty in difficulties)
        {
            if (!difficulty.Enabled) continue;
            var best = profile.Find(entry.MissionId, difficulty.Id);
            var gradeLabel = best is null ? null : GradeIds.LabelOf(best.BestGrade);
            records.Add(new MissionDifficultyRecord(
                difficulty.Id,
                difficulty.Label,
                gradeLabel,
                gradeLabel ?? strings.Get("missionSelect.noGradeYet"),
                best?.SPlusAchieved ?? false,
                best?.ObjectivesCompleted.Count ?? 0));
        }
        return new MissionCard(entry.MissionId, entry.Title, entry.Era, entry.Setting, false, null, records, records.Any(r => r.SPlusAchieved));
    }
}
