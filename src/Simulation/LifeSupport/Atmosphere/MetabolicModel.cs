using Usa.Simulation.LifeSupport.Config;

namespace Usa.Simulation.LifeSupport.Atmosphere;

/// <summary>Per-crew O2 use, CO2 output and body heat for an activity level.</summary>
public readonly record struct MetabolicRate(double O2KgPerS, double Co2KgPerS, double HeatW);

/// <summary>Looks up <see cref="MetabolicRate"/> by activity without allocation.</summary>
public sealed class MetabolicModel
{
    private readonly MetabolicRate[] _rates;

    public MetabolicModel(MetabolicConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var activities = Enum.GetValues<CrewActivity>();
        _rates = new MetabolicRate[activities.Length];
        foreach (var activity in activities)
        {
            var level = config.Activities.FirstOrDefault(a => a.Activity == activity)
                        ?? throw new ArgumentException($"No metabolic entry for activity '{activity}'.", nameof(config));
            _rates[(int)activity] = new MetabolicRate(
                config.O2KgPerDayAtRest * level.O2Multiplier / GasConstants.SecondsPerDay,
                config.Co2KgPerDayAtRest * level.Co2Multiplier / GasConstants.SecondsPerDay,
                config.HeatWAtRest * level.HeatMultiplier);
        }
    }

    public MetabolicRate RateFor(CrewActivity activity) => _rates[(int)activity];
}
