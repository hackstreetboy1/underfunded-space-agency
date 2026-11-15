using Usa.Simulation.Crew.Tasks;
using Usa.Simulation.Data;

namespace Usa.Simulation.Crew;

/// <summary>
/// Task duration and error probability (GDD 5.4):
/// duration = base × skillFactor × healthFactor × equipmentFactor (× rate impairment);
/// pError = clamp(complexity × a − experience × b + (1 − health/100) × c + base (+ rate impairment), min, max).
/// </summary>
public sealed class CrewPerformance
{
    private readonly PiecewiseLinearCurve _skillFactor;
    private readonly PiecewiseLinearCurve _healthFactor;
    private readonly RateImpairmentDefinition _rate;
    private readonly TaskErrorModelDefinition _errors;

    private CrewPerformance(
        PiecewiseLinearCurve skillFactor, PiecewiseLinearCurve healthFactor, RateImpairmentDefinition rate, TaskErrorModelDefinition errors)
    {
        _skillFactor = skillFactor;
        _healthFactor = healthFactor;
        _rate = rate;
        _errors = errors;
    }

    public double RateImpairmentThresholdDps => _rate.ThresholdDps;

    public bool IsRateImpaired(double angularRateDps) => angularRateDps > _rate.ThresholdDps;

    /// <summary>Mean skill level of <paramref name="member"/> across the task's domains.</summary>
    public static double SkillLevel(TaskDefinition task, CrewMemberDefinition member)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(member);
        if (task.Skills.Count == 0) return 1.0;
        var sum = 0.0;
        for (var i = 0; i < task.Skills.Count; i++) sum += member.Skill(task.Skills[i]);
        return sum / task.Skills.Count;
    }

    public double DurationSeconds(TaskDefinition task, double skillLevel, double health, bool rateImpaired)
    {
        ArgumentNullException.ThrowIfNull(task);
        var seconds = task.BaseDurationS * _skillFactor.Evaluate(skillLevel) * _healthFactor.Evaluate(health) * task.EquipmentFactor;
        return rateImpaired ? seconds * _rate.DurationMultiplier : seconds;
    }

    public double DurationSeconds(TaskDefinition task, CrewMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return DurationSeconds(task, SkillLevel(task, member.Definition), member.Health, IsRateImpaired(member.AngularRateDps));
    }

    public double ErrorProbability(int complexity, double experience, double health, bool rateImpaired)
    {
        var p = complexity * _errors.ComplexityWeight
                - experience * _errors.ExperienceWeight
                + (1.0 - health / 100.0) * _errors.HealthWeight
                + _errors.Base;
        if (rateImpaired) p += _rate.ErrorAdd;
        return Math.Clamp(p, _errors.Min, _errors.Max);
    }

    public double ErrorProbability(TaskDefinition task, CrewMember member)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(member);
        return ErrorProbability(task.Complexity, member.Definition.Experience, member.Health, IsRateImpaired(member.AngularRateDps));
    }

    internal static CrewPerformance? Create(CrewPerformanceDefinition definition, string file, DefinitionErrors errors)
    {
        var skill = PiecewiseLinearCurve.Create(definition.SkillDurationFactor, file, "$.performance.skillDurationFactor", errors, minY: 0.01);
        var health = PiecewiseLinearCurve.Create(definition.HealthDurationFactor, file, "$.performance.healthDurationFactor", errors, minY: 0.01);

        var rate = definition.RateImpairment;
        if (!(rate.ThresholdDps > 0)) errors.Add(file, "$.performance.rateImpairment.thresholdDps", "Threshold must be positive.");
        if (!(rate.DurationMultiplier >= 1)) errors.Add(file, "$.performance.rateImpairment.durationMultiplier", "Multiplier must be at least 1.");
        if (!(rate.ErrorAdd >= 0)) errors.Add(file, "$.performance.rateImpairment.errorAdd", "Error increase must be non-negative.");

        var e = definition.Errors;
        if (!(e.Min >= 0 && e.Max <= 1 && e.Min <= e.Max)) errors.Add(file, "$.performance.errors", "Need 0 <= min <= max <= 1.");
        if (!(double.IsFinite(e.ComplexityWeight) && double.IsFinite(e.ExperienceWeight) && double.IsFinite(e.HealthWeight) && double.IsFinite(e.Base)))
            errors.Add(file, "$.performance.errors", "Weights must be finite.");

        return skill is null || health is null ? null : new CrewPerformance(skill, health, rate, e);
    }
}
