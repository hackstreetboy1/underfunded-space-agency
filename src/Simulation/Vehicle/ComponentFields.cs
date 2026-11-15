using System.Text.Json;
using Usa.Simulation.Vehicle.Definitions;

namespace Usa.Simulation.Vehicle;

/// <summary>Field names of component state paths (<c>&lt;componentId&gt;.&lt;field&gt;</c>) and text forms of enums.</summary>
public static class ComponentFields
{
    public const string Condition = "condition";
    public const string Fault = "fault";
    public const string Attached = "attached";
    public const string Family = "family";
    public const string ValveOpen = "valve_open";
    public const string StuckFraction = "stuck_fraction";
    public const string TemperatureC = "temperature_c";
    public const string Available = "available";
    public const string Firing = "firing";
    public const string Activity = "activity";
    public const string ActivityLevel = "activity_level";
    public const string Armed = "armed";
    public const string AvailableCount = "available_count";
    public const string DriversPowered = "drivers_powered";
    public const string QuantityKg = "quantity_kg";
    public const string PropKg = "prop_kg";
    public const string CapacityKg = "capacity_kg";
    public const string Fraction = "fraction";
    public const string Depleted = "depleted";
    public const string Closed = "closed";
    public const string VoltageV = "voltage_v";
    public const string Powered = "powered";
    public const string Brownout = "brownout";
    public const string Online = "online";
    public const string OutputW = "output_w";
    public const string OutputKw = "output_kw";
    public const string Efficiency = "efficiency";
    public const string MaxPowerW = "max_power_w";
    public const string EnergyKwh = "energy_kwh";
    public const string CapacityKwh = "capacity_kwh";
    public const string On = "on";
    public const string Energized = "energized";
    public const string PowerW = "power_w";
    public const string HeatW = "heat_w";
    public const string Jumper = "jumper";
    public const string Shorted = "shorted";
    public const string Continuity = "continuity";
    public const string Live = "live";
    public const string Bias = "bias";
    public const string Latched = "latched";
    public const string LoadFraction = "load_fraction";
    public const string LoadPct = "load_pct";
    public const string OverstressS = "overstress_s";
    public const string OverstressAlarm = "overstress_alarm";
    public const string Damaged = "damaged";
    public const string State = "state";
    public const string Deployed = "deployed";
    public const string Fired = "fired";
    public const string Burning = "burning";
    public const string BurnRemainingS = "burn_remaining_s";

    /// <summary>Driver fault that holds its thruster valve open whenever the driver is energized.</summary>
    public const string ShortOnFault = "short_on";

    /// <summary>Fault recorded on a docking port damaged by sustained overstress.</summary>
    public const string OverstressFault = "overstress";

    private static readonly string[] FamilyNames = BuildFamilyNames();

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        Condition, Fault, Attached, Family, ValveOpen, StuckFraction, TemperatureC, Available, Firing, Activity,
        ActivityLevel, Armed, AvailableCount, DriversPowered, QuantityKg, PropKg, CapacityKg, Fraction, Depleted,
        Closed, VoltageV, Powered, Brownout, Online, OutputW, OutputKw, Efficiency, MaxPowerW, EnergyKwh, CapacityKwh,
        On, Energized, PowerW, HeatW, Jumper, Shorted, Continuity, Live, Bias, Latched, LoadFraction, LoadPct,
        OverstressS, OverstressAlarm, Damaged, State, Deployed, Fired, Burning, BurnRemainingS,
    };

    /// <summary>True for built-in field names, which sensor channels may not reuse.</summary>
    public static bool IsReserved(string field) => Reserved.Contains(field);

    public static string FamilyName(ComponentFamily family) => FamilyNames[(int)family];

    public static string ConditionName(ComponentCondition condition) => condition switch
    {
        ComponentCondition.Nominal => "nominal",
        ComponentCondition.Degraded => "degraded",
        ComponentCondition.Failed => "failed",
        ComponentCondition.Isolated => "isolated",
        _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, null),
    };

    public static bool TryParseCondition(string text, out ComponentCondition condition)
    {
        switch (text)
        {
            case "nominal": condition = ComponentCondition.Nominal; return true;
            case "degraded": condition = ComponentCondition.Degraded; return true;
            case "failed": condition = ComponentCondition.Failed; return true;
            case "isolated": condition = ComponentCondition.Isolated; return true;
            default: condition = default; return false;
        }
    }

    public static string ParachuteName(ParachuteStatus status) => status switch
    {
        ParachuteStatus.Stowed => "stowed",
        ParachuteStatus.Deployed => "deployed",
        ParachuteStatus.Cut => "cut",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static bool TryParseParachute(string text, out ParachuteStatus status)
    {
        switch (text)
        {
            case "stowed": status = ParachuteStatus.Stowed; return true;
            case "deployed": status = ParachuteStatus.Deployed; return true;
            case "cut": status = ParachuteStatus.Cut; return true;
            default: status = default; return false;
        }
    }

    private static string[] BuildFamilyNames()
    {
        var families = Enum.GetValues<ComponentFamily>();
        var names = new string[families.Length];
        foreach (var family in families) names[(int)family] = JsonNamingPolicy.SnakeCaseLower.ConvertName(family.ToString());
        return names;
    }
}
