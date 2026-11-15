using Usa.Game.Content;
using Usa.Game.Flow.Data;
using Usa.Game.Services.Saves;
using Usa.Game.Services.Settings;

namespace Usa.Game.Flow.Screens;

/// <summary>One slot of the briefing imagery carousel.</summary>
/// <param name="CreditLine">"NASA · S66-12345" once an item is curated; null while the slot is still a placeholder.</param>
/// <param name="LocalPath">Cached media from the manifest; null when the item is not shipped yet (show the caption on a plain panel).</param>
public sealed record BriefingImage(string SlotId, string? Caption, string? AltText, string? CreditLine, string? LocalPath)
{
    public bool HasImage => LocalPath is not null;
}

/// <summary>
/// Briefing (UI.md 5, GDD 3.3): mission text on the left (premise, objectives with success criteria, crew,
/// vehicles, known constraints, budget note), imagery with captions and credits plus the historical context on the
/// right, and the "Guided first run" toggle next to Start.
/// </summary>
public sealed class BriefingModel
{
    private readonly ScreenRouter _router;

    public BriefingModel(
        ScreenRouter router,
        ContentLibrary content,
        DifficultyInfo difficulty,
        PlayerProfile profile,
        GameplaySettings gameplay,
        NasaManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(difficulty);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(gameplay);
        ArgumentNullException.ThrowIfNull(manifest);
        _router = router;

        var briefing = content.Briefing;
        var strings = content.Strings;
        Definition = briefing;
        DifficultyId = difficulty.Id;
        DifficultyLabel = difficulty.DisplayName;
        Images = briefing.ImageSlots.Select(s => ToImage(s, manifest)).ToArray();
        BudgetTapeLabels = strings.TapeLabels
            .Where(l => l.Placement.StartsWith("Briefing", StringComparison.Ordinal))
            .Select(l => l.Text)
            .ToArray();

        ObjectivesTitle = strings.Get("briefing.objectivesTitle");
        CrewTitle = strings.Get("briefing.crewTitle");
        VehicleTitle = strings.Get("briefing.vehicleTitle");
        ConstraintsTitle = strings.Get("briefing.constraintsTitle");
        StartLabel = strings.Get("briefing.start");
        GuidedFirstRunLabel = strings.Get("briefing.guidedFirstRun");
        GuidedFirstRunHint = strings.Get("briefing.guidedFirstRunHint");

        HasPlayed = profile.Records.Any(r => string.Equals(r.MissionId, briefing.MissionId, StringComparison.Ordinal));
        GuidedFirstRunDefault = DefaultGuidedFirstRun(difficulty, HasPlayed, gameplay);
        GuidedFirstRun = GuidedFirstRunDefault;
    }

    public event Action? Changed;

    public BriefingDefinition Definition { get; }

    public string Title => Definition.Title;

    public BriefingHeader Header => Definition.Header;

    public IReadOnlyList<string> Premise => Definition.Premise;

    public IReadOnlyList<BriefingObjective> Objectives => Definition.Objectives;

    public IReadOnlyList<BriefingCrewMember> Crew => Definition.Crew;

    public IReadOnlyList<BriefingVehicle> Vehicles => Definition.Vehicles;

    public IReadOnlyList<string> KnownConstraints => Definition.KnownConstraints;

    public BudgetNote BudgetNote => Definition.BudgetNote;

    /// <summary>Tape labels stuck beside the budget note.</summary>
    public IReadOnlyList<string> BudgetTapeLabels { get; }

    /// <summary>The "Historical context" box (no program names).</summary>
    public HistoricalContext HistoricalContext => Definition.HistoricalContext;

    public IReadOnlyList<BriefingImage> Images { get; }

    public string DifficultyId { get; }

    public string DifficultyLabel { get; }

    public string ObjectivesTitle { get; }

    public string CrewTitle { get; }

    public string VehicleTitle { get; }

    public string ConstraintsTitle { get; }

    public string StartLabel { get; }

    public string GuidedFirstRunLabel { get; }

    public string GuidedFirstRunHint { get; }

    public bool HasPlayed { get; }

    public bool GuidedFirstRunDefault { get; }

    public bool GuidedFirstRun { get; private set; }

    /// <summary>
    /// On when the difficulty offers it (<c>tutorial_default</c>), the player has not finished this mission yet or the
    /// difficulty keeps it on for every play (<c>tutorial_every_play</c>; GDD 9.3: Easy "on by default", Normal "on for first
    /// play"), and tutorial callouts are not switched off in Settings (GDD 17).
    /// </summary>
    public static bool DefaultGuidedFirstRun(DifficultyInfo difficulty, bool hasPlayed, GameplaySettings gameplay)
    {
        ArgumentNullException.ThrowIfNull(difficulty);
        ArgumentNullException.ThrowIfNull(gameplay);
        return difficulty.TutorialDefault && (!hasPlayed || difficulty.TutorialEveryPlay) && gameplay.TutorialCallouts;
    }

    public void SetGuidedFirstRun(bool enabled)
    {
        if (GuidedFirstRun == enabled) return;
        GuidedFirstRun = enabled;
        Changed?.Invoke();
    }

    public bool Start() => _router.StartMission(GuidedFirstRun);

    public bool Back() => _router.Back();

    private static BriefingImage ToImage(ImageSlot slot, NasaManifest manifest)
    {
        var item = slot.NasaId is null
            ? null
            : manifest.ForTouchpoint(NasaTouchpoints.Briefing).FirstOrDefault(i => string.Equals(i.ItemId, slot.NasaId, StringComparison.Ordinal));
        var credit = slot.Credit ?? item?.Credit;
        var creditLine = credit is null ? null : slot.NasaId is null ? credit : $"{credit} · {slot.NasaId}";
        return new BriefingImage(slot.Id, slot.Caption ?? item?.DisplayCaption, slot.AltText, creditLine, item?.LocalPath);
    }
}
