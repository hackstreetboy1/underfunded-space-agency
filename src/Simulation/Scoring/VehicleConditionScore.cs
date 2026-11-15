namespace Usa.Simulation.Scoring;

/// <summary>Vehicle condition (GDD 10.1): critical systems remaining, damage containment, redundancy.</summary>
internal static class VehicleConditionScore
{
    public static DimensionScore Evaluate(VehicleTuning tuning, double weightPoints, MissionRecord record)
    {
        var dimension = new DimensionBuilder(ScoreDimension.VehicleCondition, weightPoints);
        var vehicle = record.Vehicle;

        var systems = vehicle.CriticalSystemsTotal > 0
            ? Ramp.Ratio(vehicle.CriticalSystemsRemaining, vehicle.CriticalSystemsTotal)
            : 1.0;
        var containment = 1.0 - vehicle.UncontainedDamageCount * tuning.DamagePenaltyPerEvent;
        var redundancy = vehicle.Redundancy == RedundancyStatus.Lost ? tuning.RedundancyLostCredit : 1.0;

        if (vehicle.UncontainedDamageCount > 0) dimension.Note("vehicle.uncontained_damage", null, vehicle.UncontainedDamageCount);
        dimension.Note("vehicle.redundancy." + RedundancyKey(vehicle.Redundancy));

        dimension.Line("vehicle.critical_systems", tuning.CriticalSystems, systems);
        dimension.Line("vehicle.damage_containment", tuning.DamageContainment, containment);
        dimension.Line("vehicle.redundancy", tuning.Redundancy, redundancy);
        return dimension.Build();
    }

    private static string RedundancyKey(RedundancyStatus status) => status switch
    {
        RedundancyStatus.NeverLost => "never_lost",
        RedundancyStatus.Restored => "restored",
        _ => "lost",
    };
}
