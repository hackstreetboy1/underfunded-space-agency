using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Crew;

/// <summary>
/// Effect target <c>crew.&lt;id&gt;.home_location</c> (set, text: a crew location id) moves a member's home through
/// <see cref="CrewRoster.TryMove"/>, e.g. everyone back to the ferry when the stack separates. A move the roster refuses
/// (unknown or suited location, dead member) changes nothing.
/// </summary>
public sealed class CrewMoveHandler(CrewRoster roster) : IEffectHandler
{
    public const string Field = "home_location";

    public string Prefix => StatePaths.Crew;

    public bool CanApply(string target, EffectOperation operation) => operation == EffectOperation.Set && Member(target) is not null;

    public void Apply(string target, EffectOperation operation, StateValue value, MissionTime now)
    {
        if (operation != EffectOperation.Set || Member(target) is not { } member || value.Kind != StateValueKind.Text) return;
        roster.TryMove(member.Id, value.AsText());
    }

    private CrewMember? Member(string target)
    {
        if (target is null || target.Length <= StatePaths.Crew.Length + Field.Length + 1 || !target.StartsWith(StatePaths.Crew, StringComparison.Ordinal) || !target.EndsWith("." + Field, StringComparison.Ordinal))
            return null;
        var id = target.AsSpan(StatePaths.Crew.Length, target.Length - StatePaths.Crew.Length - Field.Length - 1);
        foreach (var member in roster.Members)
        {
            if (id.SequenceEqual(member.Id.Value)) return member;
        }
        return null;
    }
}
