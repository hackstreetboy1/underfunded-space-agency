using Usa.Simulation.Commands;
using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Kernel;

namespace Usa.Game.Comms;

/// <summary>One glance read to order: the action, its arguments and the facts it reads.</summary>
public sealed record PlannedRead(CatalogAction Action, IReadOnlyDictionary<string, CommandArg> Args, IReadOnlyList<FactId> Facts);

/// <summary>
/// The gauges the crew can read right now, and the fewest glance reads that cover a set of facts: a fixed-list read
/// (all sixteen thruster temperatures at once) when it covers two or more of them, a one-fact read for the rest.
/// </summary>
public static class GlanceReadPlanner
{
    /// <summary>Every fact some allowed glance read covers, in first-seen order.</summary>
    public static IReadOnlyList<FactId> Readable(IEnumerable<CatalogAction> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        var seen = new HashSet<FactId>();
        var facts = new List<FactId>();
        foreach (var action in allowed)
        {
            foreach (var fact in Covers(action))
            {
                if (seen.Add(fact)) facts.Add(fact);
            }
        }
        return facts;
    }

    /// <summary>The reads that cover <paramref name="needed"/>, plus the facts no allowed read covers.</summary>
    public static (IReadOnlyList<PlannedRead> Reads, IReadOnlyList<FactId> Unreadable) Plan(IEnumerable<CatalogAction> allowed, IEnumerable<FactId> needed)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        ArgumentNullException.ThrowIfNull(needed);
        var actions = allowed.Where(a => a.Kind == ActionKind.GlanceRead && a.Definition.Glance is not null).ToArray();
        var remaining = new List<FactId>(needed.Distinct());
        var reads = new List<PlannedRead>();

        foreach (var action in actions)
        {
            if (action.Definition.Glance!.Facts is not { Count: > 0 } fixedFacts) continue;
            var covered = remaining.Where(f => fixedFacts.Contains(f)).ToArray();
            if (covered.Length < 2) continue;
            reads.Add(new PlannedRead(action, CommandRequest.NoArgs, fixedFacts));
            remaining.RemoveAll(covered.Contains);
        }

        var unreadable = new List<FactId>();
        foreach (var fact in remaining)
        {
            if (Single(actions, fact) is { } read) reads.Add(read);
            else unreadable.Add(fact);
        }
        return (reads, unreadable);
    }

    private static PlannedRead? Single(IEnumerable<CatalogAction> actions, FactId fact)
    {
        foreach (var action in actions)
        {
            var glance = action.Definition.Glance!;
            if (glance.FactParam is { } param)
            {
                if (action.FindParameter(param)?.Values?.Contains(fact.Value, StringComparer.Ordinal) != true) continue;
                var args = new Dictionary<string, CommandArg>(StringComparer.Ordinal) { [param] = CommandArg.Of(fact.Value) };
                return new PlannedRead(action, args, [fact]);
            }
        }
        foreach (var action in actions)
        {
            if (action.Definition.Glance!.Facts is { } fixedFacts && fixedFacts.Contains(fact))
                return new PlannedRead(action, CommandRequest.NoArgs, fixedFacts);
        }
        return null;
    }

    private static IEnumerable<FactId> Covers(CatalogAction action)
    {
        if (action.Kind != ActionKind.GlanceRead || action.Definition.Glance is not { } glance) yield break;
        if (glance.Facts is { } fixedFacts)
        {
            foreach (var fact in fixedFacts) yield return fact;
        }
        if (glance.FactParam is { } param)
        {
            foreach (var value in action.FindParameter(param)?.Values ?? []) yield return new FactId(value);
        }
    }
}
