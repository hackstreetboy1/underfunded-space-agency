using Usa.Simulation.Data;

namespace Usa.Simulation.Flight;

/// <summary>
/// The vehicles a mission's flight model propagates, by role, from <c>flight.json</c> <c>vehicles</c> and
/// <c>dockingPair</c>. Flight models one crewed vehicle and at most one target; only that pair can dock.
/// </summary>
public sealed class FlightVehicles
{
    private FlightVehicles(string crewedId, string? targetId, double targetLeadM, bool targetDocks)
    {
        CrewedId = crewedId;
        TargetId = targetId;
        TargetLeadM = targetLeadM;
        TargetDocks = targetDocks;
        Ids = targetId is null ? [crewedId] : [.. new[] { crewedId, targetId }.Order(StringComparer.Ordinal)];
    }

    public string CrewedId { get; }

    /// <summary>Null when the mission flies the crewed vehicle alone.</summary>
    public string? TargetId { get; }

    public double TargetLeadM { get; }

    /// <summary>The data names the crewed and target vehicles as the docking pair.</summary>
    public bool TargetDocks { get; }

    /// <summary>Every vehicle flight propagates, sorted ordinally.</summary>
    public IReadOnlyList<string> Ids { get; }

    public bool Contains(string vehicleId) => vehicleId == CrewedId || (TargetId is not null && vehicleId == TargetId);

    /// <summary>Validates the roles; adds every problem to <paramref name="errors"/> and returns null if there is one.</summary>
    internal static FlightVehicles? Create(
        IReadOnlyList<FlightVehicleDefinition> vehicles, IReadOnlyList<string>? dockingPair, string file, DefinitionErrors errors)
    {
        string? crewed = null;
        FlightVehicleDefinition? target = null;
        var problems = errors.All.Count;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < vehicles.Count; i++)
        {
            var vehicle = vehicles[i];
            var path = $"$.vehicles[{i}]";
            if (string.IsNullOrWhiteSpace(vehicle.Id)) errors.Add(file, path + ".id", "Vehicle id is empty.");
            else if (!ids.Add(vehicle.Id)) errors.Add(file, path + ".id", $"Vehicle '{vehicle.Id}' is listed twice.");

            switch (vehicle.Role)
            {
                case FlightVehicleRole.Crewed when crewed is not null:
                    errors.Add(file, path + ".role", "Only one vehicle can be crewed.");
                    break;
                case FlightVehicleRole.Crewed:
                    crewed = vehicle.Id;
                    if (vehicle.LeadM != 0) errors.Add(file, path + ".leadM", "Only a target vehicle leads the crewed one.");
                    break;
                case FlightVehicleRole.Target when target is not null:
                    errors.Add(file, path + ".role", "Flight models at most one target vehicle.");
                    break;
                case FlightVehicleRole.Target:
                    target = vehicle;
                    if (!(double.IsFinite(vehicle.LeadM) && vehicle.LeadM >= 0)) errors.Add(file, path + ".leadM", "Must be finite and not negative.");
                    break;
            }
        }
        if (crewed is null) errors.Add(file, "$.vehicles", "One vehicle must have the role 'crewed'.");

        var docks = false;
        if (dockingPair is not null)
        {
            var pairNamesCrewedAndTarget = dockingPair.Count == 2 && crewed is not null && target is not null &&
                                           dockingPair.Contains(crewed) && dockingPair.Contains(target.Id) && crewed != target.Id;
            if (!pairNamesCrewedAndTarget)
                errors.Add(file, "$.dockingPair", "The docking pair must list the crewed vehicle and the target vehicle.");
            docks = pairNamesCrewedAndTarget;
        }

        return errors.All.Count > problems || crewed is null ? null : new FlightVehicles(crewed, target?.Id, target?.LeadM ?? 0, docks);
    }
}
