using Usa.Simulation.Data;

namespace Usa.Game.Content;

/// <summary>Loads and validates a mission briefing.</summary>
public static class BriefingLoader
{
    /// <exception cref="DefinitionException">Every problem in the file at once.</exception>
    public static BriefingDefinition Load(IDataSource source, string missionId)
    {
        var file = BriefingDefinition.PathFor(missionId);
        var briefing = new DefinitionLoader(source).Load<BriefingDefinition>(file, BriefingDefinition.SchemaTag);
        var errors = new DefinitionErrors();
        Validate(briefing, file, missionId, errors);
        errors.ThrowIfAny();
        return briefing;
    }

    public static void Validate(BriefingDefinition briefing, string file, string missionId, DefinitionErrors errors)
    {
        ArgumentNullException.ThrowIfNull(briefing);
        ArgumentNullException.ThrowIfNull(errors);

        if (briefing.MissionId != missionId) errors.Add(file, "$.missionId", $"Expected mission '{missionId}' but found '{briefing.MissionId}'.");
        ContentChecks.Text(errors, file, "$.title", briefing.Title);

        var header = briefing.Header;
        ContentChecks.Text(errors, file, "$.header.agency", header.Agency);
        ContentChecks.Text(errors, file, "$.header.vehicles", header.Vehicles);
        ContentChecks.Text(errors, file, "$.header.era", header.Era);
        ContentChecks.Text(errors, file, "$.header.orbit", header.Orbit);
        ContentChecks.Text(errors, file, "$.header.missionStart", header.MissionStart);

        ContentChecks.Texts(errors, file, "$.premise", briefing.Premise);
        ContentChecks.Texts(errors, file, "$.knownConstraints", briefing.KnownConstraints);

        ValidateObjectives(briefing, file, errors);
        ValidateCrew(briefing, file, errors);
        ValidateVehicles(briefing, file, errors);

        ContentChecks.Text(errors, file, "$.budgetNote.title", briefing.BudgetNote.Title);
        ContentChecks.Text(errors, file, "$.budgetNote.text", briefing.BudgetNote.Text);
        ContentChecks.Text(errors, file, "$.budgetNote.signedBy", briefing.BudgetNote.SignedBy);
        ContentChecks.Text(errors, file, "$.historicalContext.title", briefing.HistoricalContext.Title);
        ContentChecks.Text(errors, file, "$.historicalContext.label", briefing.HistoricalContext.Label);
        ContentChecks.Text(errors, file, "$.historicalContext.text", briefing.HistoricalContext.Text);

        ValidateImageSlots(briefing.ImageSlots, file, errors);
    }

    private static void ValidateObjectives(BriefingDefinition briefing, string file, DefinitionErrors errors)
    {
        if (briefing.Objectives.Count == 0) errors.Add(file, "$.objectives", "At least one objective is required.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < briefing.Objectives.Count; i++)
        {
            var objective = briefing.Objectives[i];
            var path = $"$.objectives[{i}]";
            ContentChecks.UniqueId(errors, file, path + ".id", ids, objective.Id.Value, "objective");
            ContentChecks.Text(errors, file, path + ".title", objective.Title);
            ContentChecks.Text(errors, file, path + ".summary", objective.Summary);
            ContentChecks.Texts(errors, file, path + ".successCriteria", objective.SuccessCriteria);
            ContentChecks.Text(errors, file, path + ".weightNote", objective.WeightNote);
        }
    }

    private static void ValidateCrew(BriefingDefinition briefing, string file, DefinitionErrors errors)
    {
        if (briefing.Crew.Count == 0) errors.Add(file, "$.crew", "At least one crew member is required.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < briefing.Crew.Count; i++)
        {
            var member = briefing.Crew[i];
            var path = $"$.crew[{i}]";
            ContentChecks.UniqueId(errors, file, path + ".id", ids, member.Id.Value, "crew");
            ContentChecks.Text(errors, file, path + ".name", member.Name);
            ContentChecks.Text(errors, file, path + ".role", member.Role);
            ContentChecks.Text(errors, file, path + ".bio", member.Bio);
            ContentChecks.Text(errors, file, path + ".nameTag", member.NameTag);
            ContentChecks.Texts(errors, file, path + ".strengths", member.Strengths);
            if (member.Age is < 18 or > 99) errors.Add(file, path + ".age", "Age must be between 18 and 99.");
        }
    }

    private static void ValidateVehicles(BriefingDefinition briefing, string file, DefinitionErrors errors)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < briefing.Vehicles.Count; i++)
        {
            var vehicle = briefing.Vehicles[i];
            var path = $"$.vehicles[{i}]";
            ContentChecks.UniqueId(errors, file, path + ".id", ids, vehicle.Id, "vehicle");
            ContentChecks.Text(errors, file, path + ".name", vehicle.Name);
            ContentChecks.Text(errors, file, path + ".summary", vehicle.Summary);
            for (var s = 0; s < vehicle.Sections.Count; s++)
            {
                ContentChecks.Text(errors, file, $"{path}.sections[{s}].name", vehicle.Sections[s].Name);
                ContentChecks.Text(errors, file, $"{path}.sections[{s}].text", vehicle.Sections[s].Text);
            }
        }
    }

    private static void ValidateImageSlots(IReadOnlyList<ImageSlot> slots, string file, DefinitionErrors errors)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var path = $"$.imageSlots[{i}]";
            ContentChecks.UniqueId(errors, file, path + ".id", ids, slot.Id, "image slot");
            ContentChecks.Text(errors, file, path + ".purpose", slot.Purpose);
            ContentChecks.Text(errors, file, path + ".caption", slot.Caption);
            ContentChecks.Text(errors, file, path + ".altText", slot.AltText);
            ContentChecks.OptionalText(errors, file, path + ".credit", slot.Credit);
            ContentChecks.OptionalText(errors, file, path + ".nasaId", slot.NasaId);
            if (slot.NasaId is not null && slot.Credit is null)
                errors.Add(file, path + ".credit", "A picked item (nasaId set) needs its credit line.");
        }
    }
}
