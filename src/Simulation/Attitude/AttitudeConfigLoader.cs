using System.Globalization;
using Usa.Simulation.Data;

namespace Usa.Simulation.Attitude;

/// <summary>Loads and validates <c>config/attitude.json</c>.</summary>
public static class AttitudeConfigLoader
{
    public const string RelativePath = "config/attitude.json";
    public const string Schema = "attitude/1";

    public static AttitudeConfigDefinition Load(IDataSource source)
    {
        var config = new DefinitionLoader(source).Load<AttitudeConfigDefinition>(RelativePath, Schema);
        var errors = new DefinitionErrors();
        Validate(config, RelativePath, errors);
        errors.ThrowIfAny();
        return config;
    }

    /// <summary>Checks the file on its own; references to vehicle thrusters are checked by <see cref="AttitudeSystem"/>.</summary>
    public static void Validate(AttitudeConfigDefinition config, string file, DefinitionErrors errors)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(errors);

        var thresholds = config.RateThresholdsDegPerSec;
        for (var i = 0; i < thresholds.Count; i++)
        {
            if (!(double.IsFinite(thresholds[i]) && thresholds[i] > 0))
                errors.Add(file, $"$.rateThresholdsDegPerSec[{i}]", "Thresholds must be positive.");
            else if (i > 0 && thresholds[i] <= thresholds[i - 1])
                errors.Add(file, $"$.rateThresholdsDegPerSec[{i}]", "Thresholds must be strictly ascending.");
        }

        var hysteresis = config.ThresholdHysteresisDegPerSec;
        if (!(double.IsFinite(hysteresis) && hysteresis >= 0))
            errors.Add(file, "$.thresholdHysteresisDegPerSec", "Must be zero or positive.");
        else if (thresholds.Count > 0 && hysteresis >= thresholds[0])
            errors.Add(file, "$.thresholdHysteresisDegPerSec", "Must be below the smallest threshold.");

        if (!(config.MinAxisTorqueFraction >= 0 && config.MinAxisTorqueFraction < 1))
            errors.Add(file, "$.minAxisTorqueFraction", "Must be in [0, 1).");
        RequirePositive(config.FiringDutyTimeConstantS, file, "$.firingDutyTimeConstantS", errors);

        ValidateRateLaw(config.RateDamp.DeadbandDegPerSec, config.RateDamp.SettleDegPerSec, config.RateDamp.RateTimeConstantS, file, "$.rateDamp", errors);
        var hold = config.AttitudeHold;
        ValidateRateLaw(hold.DeadbandDegPerSec, hold.SettleDegPerSec, hold.RateTimeConstantS, file, "$.attitudeHold", errors);
        RequirePositive(hold.PointingGainPerS, file, "$.attitudeHold.pointingGainPerS", errors);
        RequirePositive(hold.MaxSlewDegPerSec, file, "$.attitudeHold.maxSlewDegPerSec", errors);

        var vehicleIds = new HashSet<string>(StringComparer.Ordinal);
        for (var v = 0; v < config.Vehicles.Count; v++)
        {
            var vehicle = config.Vehicles[v];
            var path = $"$.vehicles[{v}]";
            if (string.IsNullOrEmpty(vehicle.Id)) errors.Add(file, path + ".id", "Vehicle id is empty.");
            else if (!vehicleIds.Add(vehicle.Id)) errors.Add(file, path + ".id", $"Vehicle '{vehicle.Id}' is listed twice.");

            var setIds = new HashSet<string>(StringComparer.Ordinal);
            for (var s = 0; s < vehicle.ControlSets.Count; s++)
            {
                var set = vehicle.ControlSets[s];
                var setPath = $"{path}.controlSets[{s}]";
                if (string.IsNullOrEmpty(set.Id)) errors.Add(file, setPath + ".id", "Control set id is empty.");
                else if (!setIds.Add(set.Id)) errors.Add(file, setPath + ".id", $"Control set '{set.Id}' is listed twice.");
                if ((set.Thrusters?.Count ?? 0) + (set.Groups?.Count ?? 0) == 0)
                    errors.Add(file, setPath, "A control set needs at least one thruster or group.");
            }

            if (!setIds.Contains(vehicle.InitialControlSet))
                errors.Add(file, path + ".initialControlSet", $"Control set '{vehicle.InitialControlSet}' is not defined for '{vehicle.Id}'.");
        }

        for (var d = 0; d < config.Docking.Count; d++)
        {
            var docking = config.Docking[d];
            var path = $"$.docking[{d}]";
            if (!vehicleIds.Contains(docking.Primary)) errors.Add(file, path + ".primary", $"Unknown vehicle '{docking.Primary}'.");
            if (!vehicleIds.Contains(docking.Secondary)) errors.Add(file, path + ".secondary", $"Unknown vehicle '{docking.Secondary}'.");
            if (docking.Primary == docking.Secondary) errors.Add(file, path, "A vehicle cannot dock with itself.");
            if (!double.IsFinite(docking.SecondaryComOffsetXM) || docking.SecondaryComOffsetXM == 0)
                errors.Add(file, path + ".secondaryComOffsetXM", "Offset must be finite and non-zero.");
            for (var other = 0; other < d; other++)
            {
                var previous = config.Docking[other];
                var samePair = (previous.Primary == docking.Primary && previous.Secondary == docking.Secondary) ||
                               (previous.Primary == docking.Secondary && previous.Secondary == docking.Primary);
                if (samePair) errors.Add(file, path, $"Pair {docking.Primary}/{docking.Secondary} is listed twice.");
            }
        }
    }

    private static void ValidateRateLaw(double deadband, double settle, double timeConstant, string file, string path, DefinitionErrors errors)
    {
        RequirePositive(deadband, file, path + ".deadbandDegPerSec", errors);
        if (!(settle >= 0 && settle < deadband)) errors.Add(file, path + ".settleDegPerSec", "Must be zero or positive and below the deadband.");
        var minimum = ControllerTuning.MinTimeConstantS;
        if (!(double.IsFinite(timeConstant) && timeConstant >= minimum))
            errors.Add(file, path + ".rateTimeConstantS",
                $"Must be at least {minimum.ToString(CultureInfo.InvariantCulture)} s ({ControllerTuning.MinTimeConstantSteps} controller steps) to keep the rate loop stable.");
    }

    private static void RequirePositive(double value, string file, string path, DefinitionErrors errors)
    {
        if (!(double.IsFinite(value) && value > 0)) errors.Add(file, path, "Must be positive.");
    }
}
