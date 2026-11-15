using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Knowledge.Catalog;
using Usa.Simulation.Session;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// Facts the ground is told (<see cref="FactSourceKind.Report"/>: crew reports, confirmed commands, the recovery forces), found
/// by the report path they read (<c>@report.&lt;field&gt;</c>) or, failing that, by their id. A mission without the fact
/// resolves to null, and its readings are simply unknown.
/// </summary>
public static class ReportFacts
{
    /// <summary>The fact reading <c>@report.&lt;<paramref name="reportField"/>&gt;</c>, else the fact <paramref name="fallbackId"/>.</summary>
    public static FactId? Resolve(FactCatalog facts, string reportField, string fallbackId)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var path = FactCatalog.ReportPrefix + reportField;
        foreach (var fact in facts.Facts)
        {
            if (fact.ValuePath is { } valuePath && valuePath.TrimStart('@') == path) return fact.Id;
        }
        return facts.TryResolve(new FactId(fallbackId), out var named) ? named.Id : null;
    }

    /// <summary>The latest report of <paramref name="fact"/>; null without the fact or before the first report.</summary>
    public static Observation? Latest(MissionSession session, FactId? fact)
    {
        ArgumentNullException.ThrowIfNull(session);
        return fact is { } id ? session.Knowledge.Describe(id).Latest : null;
    }
}
