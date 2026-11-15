using Usa.Simulation.Kernel;

namespace Usa.Simulation.Crew.Health;

/// <summary>
/// Per-astronaut health at 1 s (SCRIPTING 8.4): damage per second is the sum of every cause's curve at its
/// exposure; when every exposure is harmless, health recovers toward launch health. Publishes
/// <see cref="CrewHealthBandChanged"/> and <see cref="CrewMemberDied"/>.
/// </summary>
public sealed class HealthModel(HealthConfig config, CrewRoster roster, ICrewExposureSource exposures, EventBus events) : ISimSystem
{
    public int PeriodTicks => SimulationClock.TicksPerSecond;

    public void Step(in StepContext context)
    {
        var members = roster.MemberArray;
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if (!member.IsAlive) continue;
            Apply(member, exposures.Read(member.Id), context.DeltaSeconds, context.Now);
        }
    }

    /// <summary>Advances one member's health by <paramref name="deltaSeconds"/> under <paramref name="exposure"/>.</summary>
    public void Apply(CrewMember member, in CrewExposure exposure, double deltaSeconds, MissionTime now)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (!member.IsAlive) return;

        member.AngularRateDps = exposure.AngularRateDps;
        var harmfulForS = member.HarmfulForS;
        Span<double> causeDamage = stackalloc double[HealthConfig.CauseCount];
        var damage = 0.0;
        var harmless = true;
        for (var i = 0; i < HealthConfig.CauseCount; i++)
        {
            var cause = (HealthCause)i;
            var ratePerS = config.DamagePerS(cause, exposure.InputFor(cause));
            if (ratePerS <= 0)
            {
                harmfulForS[i] = 0;
                continue;
            }
            harmless = false;
            harmfulForS[i] += deltaSeconds;
            // Only the part of this step after the sustain window has elapsed does harm.
            var damagingS = Math.Min(deltaSeconds, harmfulForS[i] - config.SustainS(cause));
            if (damagingS <= 0) continue;
            causeDamage[i] = ratePerS * damagingS;
            damage += causeDamage[i];
        }

        // Damage beyond the health that was left is not recorded, so mission damage never exceeds health lost.
        var recordedShare = damage > member.Health ? member.Health / damage : 1.0;
        var damageByCause = member.DamageByCause;
        for (var i = 0; i < HealthConfig.CauseCount; i++) damageByCause[i] += causeDamage[i] * recordedShare;

        var health = member.Health - damage;
        if (harmless) health = Math.Min(member.LaunchHealth, health + config.RecoveryPerS * deltaSeconds);
        health = Math.Max(0.0, health);
        member.Health = health;
        if (health < member.WorstHealth) member.WorstHealth = health;

        var band = config.BandFor(health);
        if (band == member.Band) return;
        var previous = member.Band;
        member.Band = band;
        events.Publish(new CrewHealthBandChanged(now, member.Id, previous, band, health));
        if (band == HealthBand.Deceased) events.Publish(new CrewMemberDied(now, member.Id, PrimaryCause(member)));
    }

    public CrewHealthSummary Summarize(CrewMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var causes = new List<HealthCauseTotal>();
        for (var i = 0; i < HealthConfig.CauseCount; i++)
        {
            var cause = (HealthCause)i;
            var damage = member.DamageFrom(cause);
            if (damage > 0) causes.Add(new HealthCauseTotal(cause, config.Label(cause), damage));
        }
        causes.Sort((a, b) => b.Damage != a.Damage ? b.Damage.CompareTo(a.Damage) : a.Cause.CompareTo(b.Cause));
        return new CrewHealthSummary(
            member.Id, member.IsAlive, member.LaunchHealth, member.WorstHealth, member.Health,
            member.MissionDamage, roster.HasComplication(member), causes);
    }

    /// <summary>Summaries for every member in id order.</summary>
    public IReadOnlyList<CrewHealthSummary> Summarize()
    {
        var members = roster.MemberArray;
        var summaries = new CrewHealthSummary[members.Length];
        for (var i = 0; i < members.Length; i++) summaries[i] = Summarize(members[i]);
        return summaries;
    }

    /// <summary>Crew members whose mission damage exceeds the complication threshold (GDD 10.3).</summary>
    public int ComplicationCount()
    {
        var count = 0;
        foreach (var member in roster.MemberArray)
        {
            if (roster.HasComplication(member)) count++;
        }
        return count;
    }

    private static HealthCause PrimaryCause(CrewMember member)
    {
        var primary = HealthCause.Injury;
        var most = -1.0;
        for (var i = 0; i < HealthConfig.CauseCount; i++)
        {
            var damage = member.DamageFrom((HealthCause)i);
            if (damage > most)
            {
                most = damage;
                primary = (HealthCause)i;
            }
        }
        return primary;
    }
}
