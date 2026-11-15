using Usa.Simulation.Data;

namespace Usa.Game.Content;

/// <summary>
/// One entry point for a mission's text content: briefing, debrief, encyclopedia, UI strings and crew phrases.
/// Loading checks every file and the references between them and reports all problems at once.
/// </summary>
public sealed class ContentLibrary
{
    private ContentLibrary(
        string missionId,
        BriefingDefinition briefing,
        DebriefDefinition debrief,
        Encyclopedia encyclopedia,
        StringTable strings,
        CrewPhraseBook crewPhrases,
        DebriefMissionReferences? missionReferences)
    {
        MissionId = missionId;
        Briefing = briefing;
        Debrief = debrief;
        Encyclopedia = encyclopedia;
        Strings = strings;
        CrewPhrases = crewPhrases;
        DebriefComposer = new DebriefComposer(debrief, briefing, missionReferences);
    }

    public string MissionId { get; }

    public BriefingDefinition Briefing { get; }

    public DebriefDefinition Debrief { get; }

    public Encyclopedia Encyclopedia { get; }

    public StringTable Strings { get; }

    public CrewPhraseBook CrewPhrases { get; }

    public DebriefComposer DebriefComposer { get; }

    /// <exception cref="DefinitionException">Every problem in every file at once.</exception>
    public static ContentLibrary Load(IDataSource source, string missionId)
    {
        var errors = new DefinitionErrors();
        var library = TryLoad(source, missionId, errors);
        errors.ThrowIfAny();
        return library!;
    }

    /// <summary>Loads what it can and adds every problem to <paramref name="errors"/>; null when anything failed.</summary>
    public static ContentLibrary? TryLoad(IDataSource source, string missionId, DefinitionErrors errors)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        ArgumentNullException.ThrowIfNull(errors);
        var loader = new DefinitionLoader(source);

        var briefingFile = BriefingDefinition.PathFor(missionId);
        var briefing = ContentChecks.TryLoad(errors, briefingFile,
            () => loader.Load<BriefingDefinition>(briefingFile, BriefingDefinition.SchemaTag));
        if (briefing is not null) BriefingLoader.Validate(briefing, briefingFile, missionId, errors);

        var debriefFile = DebriefDefinition.PathFor(missionId);
        var missionReferences = DebriefMissionReferences.Load(source, missionId, errors);
        var debrief = ContentChecks.TryLoad(errors, debriefFile,
            () => loader.Load<DebriefDefinition>(debriefFile, DebriefDefinition.SchemaTag));
        if (debrief is not null)
            DebriefLoader.Validate(debrief, debriefFile, missionId, errors, missionReferences);

        var encyclopedia = ContentChecks.TryLoad(errors, EncyclopediaDefinition.RelativePath,
            () => loader.Load<EncyclopediaDefinition>(EncyclopediaDefinition.RelativePath, EncyclopediaDefinition.SchemaTag));
        if (encyclopedia is not null) Encyclopedia.Validate(encyclopedia, EncyclopediaDefinition.RelativePath, errors);

        var strings = ContentChecks.TryLoad(errors, StringsDefinition.RelativePath,
            () => loader.Load<StringsDefinition>(StringsDefinition.RelativePath, StringsDefinition.SchemaTag));
        if (strings is not null) StringTable.Validate(strings, StringsDefinition.RelativePath, errors);

        var phrases = ContentChecks.TryLoad(errors, CrewPhrasesDefinition.RelativePath,
            () => loader.Load<CrewPhrasesDefinition>(CrewPhrasesDefinition.RelativePath, CrewPhrasesDefinition.SchemaTag));
        if (phrases is not null) CrewPhraseBook.Validate(phrases, CrewPhrasesDefinition.RelativePath, errors);

        if (briefing is not null && debrief is not null) ValidateDebriefReferences(briefing, debrief, debriefFile, errors);
        if (briefing is not null && phrases is not null) ValidateCrewPhrases(briefing, phrases, errors);

        if (errors.HasErrors || briefing is null || debrief is null || encyclopedia is null || strings is null || phrases is null) return null;
        return new ContentLibrary(
            missionId, briefing, debrief, new Encyclopedia(encyclopedia), new StringTable(strings), new CrewPhraseBook(phrases), missionReferences);
    }

    private static void ValidateDebriefReferences(BriefingDefinition briefing, DebriefDefinition debrief, string file, DefinitionErrors errors)
    {
        if (debrief.HistoryCard.ImageSlotId is { } slotId && !briefing.ImageSlots.Any(s => s.Id == slotId))
            errors.Add(file, "$.historyCard.imageSlotId", $"Image slot '{slotId}' is not in the mission briefing.");

        var objectives = briefing.Objectives.Select(o => o.Id.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var key in debrief.FeasibilityReasons.Keys)
        {
            var dot = key.IndexOf('.', StringComparison.Ordinal);
            if (dot <= 0) continue; // reported by DebriefLoader
            var owner = key[..dot];
            if (owner != DebriefDefinition.MissionRuleReasonPrefix && !objectives.Contains(owner))
                errors.Add(file, ContentChecks.KeyPath("$.feasibilityReasons", key), $"'{owner}' is not a briefing objective id or '{DebriefDefinition.MissionRuleReasonPrefix}'.");
        }
    }

    /// <summary>Every crew member flying the mission needs at least one opener of every kind.</summary>
    private static void ValidateCrewPhrases(BriefingDefinition briefing, CrewPhrasesDefinition phrases, DefinitionErrors errors)
    {
        foreach (var member in briefing.Crew)
        {
            var path = ContentChecks.KeyPath("$.members", member.Id.Value);
            if (!phrases.Members.TryGetValue(member.Id, out var entries))
            {
                errors.Add(CrewPhrasesDefinition.RelativePath, path, $"Crew member '{member.Id}' of mission '{briefing.MissionId}' has no phrases.");
                continue;
            }
            foreach (var kind in Enum.GetValues<CrewPhraseKind>())
            {
                if (!entries.Any(e => e.Category == kind))
                    errors.Add(CrewPhrasesDefinition.RelativePath, path, $"No '{ContentChecks.SnakeName(kind)}' phrase.");
            }
        }
    }
}
