using Usa.Game.Presentation.Console.Snapshots;
using Usa.Simulation.Kernel;

namespace Usa.Game.Integration.Adapters;

/// <summary>A resource-strip tile (UI.md 3.2): which facts feed its values and which tab it opens.</summary>
/// <param name="PrimaryFact">Null when no fact describes the resource yet (the tile stays UNKNOWN).</param>
/// <param name="DetailFact">A text fact whose known value is shown upper-case as the tile's state word.</param>
public sealed record ResourceTileSpec(
    string Id, string Label, SystemsTab Tab, FactId? PrimaryFact, FactId? SecondaryFact = null, FactId? DetailFact = null);

/// <summary>Facts whose id starts with <paramref name="Prefix"/> form the readout group <paramref name="GroupId"/>.</summary>
public sealed record FactGroupSpec(string Prefix, SystemsTab Tab, string GroupId, string Title);

/// <summary>
/// Mission-console layout over the fact catalog and vehicle ids: resource-strip tiles, systems-board readout groups and
/// which tab a component's control belongs to. Ids follow <c>catalog/facts.json</c> and <c>vehicles/*.json</c>; the only
/// vehicle-specific part is the crewed vehicle's body-rate group (<see cref="For"/>).
/// </summary>
public sealed record ConsoleLayout(IReadOnlyList<ResourceTileSpec> Tiles, IReadOnlyList<FactGroupSpec> Groups)
{
    /// <summary>Mission 1's layout (crewed vehicle <c>tandem</c>).</summary>
    public static ConsoleLayout MissionOne { get; } = For("tandem");

    /// <summary>The console layout for a mission whose crewed vehicle is <paramref name="crewedVehicleId"/> (<c>MissionData.CrewedVehicleId</c>).</summary>
    public static ConsoleLayout For(string crewedVehicleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(crewedVehicleId);
        return new(
        [
            new("o2", "O2", SystemsTab.LifeSupport, new FactId("ls.o2_kg")),
            new("co2", "CO2 removal", SystemsTab.LifeSupport, new FactId("ls.lioh_pct"), new FactId("ls.ppco2_mmhg")),
            new("power", "Power", SystemsTab.Power, new FactId("power.load_kw"), new FactId("power.battery_kwh")),
            new("oams", "OAMS", SystemsTab.PropAttitude, new FactId("oams.prop_kg")),
            new("rcs_a", "RCS A", SystemsTab.PropAttitude, new FactId("rcs.ring_a.prop_kg"), DetailFact: new FactId("rcs.ring_a.status")),
            new("rcs_b", "RCS B", SystemsTab.PropAttitude, new FactId("rcs.ring_b.prop_kg"), DetailFact: new FactId("rcs.ring_b.status")),
            new("water", "Water", SystemsTab.LifeSupport, new FactId("ls.water_l")),
            new("food", "Food", SystemsTab.LifeSupport, new FactId("ls.food_kcal")),
        ],
        [
            new($"{crewedVehicleId}.rate.", SystemsTab.PropAttitude, "rates", "Body rates"),
            new("oams.temp.", SystemsTab.PropAttitude, "thruster_temps", "OAMS thruster temperatures"),
            new("oams.", SystemsTab.PropAttitude, "oams", "OAMS"),
            new("rcs.", SystemsTab.PropAttitude, "rcs", "RCS rings"),
            new("power.fc", SystemsTab.Power, "fuel_cells", "Fuel cells"),
            new("power.battery", SystemsTab.Power, "battery", "Re-entry battery"),
            new("power.", SystemsTab.Power, "buses", "Main buses"),
            new("ls.cabin_temp", SystemsTab.Thermal, "cabin_thermal", "Cabin temperature"),
            new("ls.", SystemsTab.LifeSupport, "cabin", "Cabin and consumables"),
            new("docking.", SystemsTab.StructureLanding, "docking", "Docking adapter"),
        ]);
    }

    /// <summary>Longest matching prefix, or null for facts the board does not show (crew and component conditions).</summary>
    public FactGroupSpec? GroupOf(FactId fact)
    {
        FactGroupSpec? best = null;
        foreach (var group in Groups)
        {
            if (fact.Value.StartsWith(group.Prefix, StringComparison.Ordinal) && (best is null || group.Prefix.Length > best.Prefix.Length))
                best = group;
        }
        return best;
    }

    /// <summary>
    /// The systems-board tab of a control, by the subsystem it switches (a breaker belongs with its load): the second segment
    /// of a <c>vehicle/subsystem/…</c> component id, whatever the vehicle.
    /// </summary>
    public static SystemsTab TabOf(ComponentId control, ComponentId? switched)
    {
        var subsystem = Subsystem((switched ?? control).Value);
        if (subsystem is "oams" or "rcs" || Starts(subsystem, "acs") || Starts(subsystem, "engine")) return SystemsTab.PropAttitude;
        if (subsystem == "thermal") return SystemsTab.Thermal;
        if (subsystem == "ls") return SystemsTab.LifeSupport;
        if (subsystem is "chute" or "retro" || Starts(subsystem, "docking")) return SystemsTab.StructureLanding;
        return SystemsTab.Power;
    }

    private static bool Starts(string subsystem, string prefix) => subsystem.StartsWith(prefix, StringComparison.Ordinal);

    private static string Subsystem(string componentId)
    {
        var first = componentId.IndexOf('/', StringComparison.Ordinal);
        if (first < 0) return string.Empty;
        var second = componentId.IndexOf('/', first + 1);
        return second < 0 ? componentId[(first + 1)..] : componentId[(first + 1)..second];
    }
}
