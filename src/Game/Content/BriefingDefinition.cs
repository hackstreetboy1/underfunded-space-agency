using Usa.Simulation.Data;
using Usa.Simulation.Kernel;

namespace Usa.Game.Content;

/// <summary>Root of <c>content/&lt;missionId&gt;/briefing.json</c> (schema <c>briefing/1</c>).</summary>
/// <param name="CurationNotes">For the team, never shown to the player.</param>
public sealed record BriefingDefinition(
    string Schema,
    string MissionId,
    string Title,
    BriefingHeader Header,
    IReadOnlyList<string> Premise,
    IReadOnlyList<BriefingObjective> Objectives,
    IReadOnlyList<BriefingCrewMember> Crew,
    IReadOnlyList<BriefingVehicle> Vehicles,
    IReadOnlyList<string> KnownConstraints,
    BudgetNote BudgetNote,
    HistoricalContext HistoricalContext,
    IReadOnlyList<ImageSlot> ImageSlots,
    string? CurationNotes = null) : IVersionedDefinition
{
    public const string SchemaTag = "briefing/1";

    public static string PathFor(string missionId) => $"content/{missionId}/briefing.json";
}

public sealed record BriefingHeader(string Agency, string Vehicles, string Era, string Orbit, string MissionStart);

public sealed record BriefingObjective(
    ObjectiveId Id,
    string Title,
    string Summary,
    IReadOnlyList<string> SuccessCriteria,
    string WeightNote);

public sealed record BriefingCrewMember(
    CrewId Id,
    string Name,
    string Role,
    int Age,
    string Bio,
    IReadOnlyList<string> Strengths,
    string NameTag);

public sealed record BriefingVehicle(string Id, string Name, string Summary, IReadOnlyList<BriefingVehicleSection> Sections);

public sealed record BriefingVehicleSection(string Name, string Text);

public sealed record BudgetNote(string Title, string Text, string SignedBy);

/// <param name="Label">Provenance label, e.g. "Documented history".</param>
public sealed record HistoricalContext(string Title, string Label, string Text);

/// <summary>A curated image placeholder; <see cref="Credit"/> and <see cref="NasaId"/> are filled once an item is picked.</summary>
/// <param name="Caption">Required: captions are rewritten by hand and shown with every image.</param>
public sealed record ImageSlot(
    string Id,
    string Purpose,
    IReadOnlyList<string> SuggestedQueries,
    string? Caption = null,
    string? AltText = null,
    string? Credit = null,
    string? NasaId = null);
