using System.Globalization;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;
using Usa.Simulation.Session;

namespace Usa.Game.Diagnostics.Harness;

/// <summary>One line of the harness state table.</summary>
public readonly record struct HarnessRow(string Key, string Label, string Value, string Detail);

/// <summary>
/// Builds the harness state table from a snapshot. In knowledge mode a reading shows what the crew and ground know (the
/// fact's latest value, its state and MET); in truth mode it shows the simulation value. Values without a fact are shown
/// only in truth mode.
/// </summary>
public static class HarnessStateRows
{
    /// <summary>
    /// Readings with a fact, as (key, label, truth state path, fact id, unit), for the mission's crewed vehicle
    /// (<c>MissionData.CrewedVehicleId</c>). A vehicle without one of these subsystems shows "n/a" in truth mode.
    /// </summary>
    public static IReadOnlyList<(string Key, string Label, string TruthPath, string FactId, string Unit)> Readings(string vehicle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vehicle);
        return
        [
            ("rate.roll", "Roll rate", $"attitude.{vehicle}.rate_roll_dps", $"{vehicle}.rate.roll_dps", "°/s"),
            ("rate.pitch", "Pitch rate", $"attitude.{vehicle}.rate_pitch_dps", $"{vehicle}.rate.pitch_dps", "°/s"),
            ("rate.yaw", "Yaw rate", $"attitude.{vehicle}.rate_yaw_dps", $"{vehicle}.rate.yaw_dps", "°/s"),
            ("prop.oams", "OAMS propellant", $"{vehicle}/oams/tank.quantity_kg", "oams.prop_kg", "kg"),
            ("prop.rcs_a", "RCS A propellant", $"{vehicle}/rcs/tank_a.quantity_kg", "rcs.ring_a.prop_kg", "kg"),
            ("prop.rcs_b", "RCS B propellant", $"{vehicle}/rcs/tank_b.quantity_kg", "rcs.ring_b.prop_kg", "kg"),
            ("bus.a", "Bus A voltage", $"{vehicle}/power/bus_a.voltage_v", "power.bus_a_v", "V"),
            ("bus.b", "Bus B voltage", $"{vehicle}/power/bus_b.voltage_v", "power.bus_b_v", "V"),
            ("cabin.ppco2", "Cabin ppCO2", $"cabin.{vehicle}.ppco2_mmhg", "ls.ppco2_mmhg", "mmHg"),
            ("cabin.pressure", "Cabin pressure", $"cabin.{vehicle}.pressure_kpa", "ls.cabin_pressure_kpa", "kPa"),
        ];
    }

    /// <param name="vehicle">The mission's crewed vehicle id (<c>MissionData.CrewedVehicleId</c>).</param>
    public static List<HarnessRow> Build(SessionSnapshot snapshot, IStateQuery state, bool truth, string vehicle)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(state);
        var facts = new Dictionary<string, FactView>(StringComparer.Ordinal);
        foreach (var fact in snapshot.Facts) facts[fact.Id.Value] = fact;

        var rows = new List<HarnessRow>
        {
            new("met", "MET", snapshot.Now.ToString(), snapshot.IsFrozen ? $"frozen: {snapshot.FreezeReasons}" : "running"),
            new("phase", "Mission phase", snapshot.Phase, snapshot.Ended ? $"ended: {snapshot.Outcome}" : ""),
        };
        if (truth)
        {
            rows.Add(new("flight", "Flight", snapshot.Flight.Phase,
                $"alt {Number(snapshot.Flight.AltitudeKm)} km, Pe {Number(snapshot.Flight.PeriapsisKm)} / Ap {Number(snapshot.Flight.ApoapsisKm)} km{(snapshot.Flight.Docked ? ", docked" : "")}"));
            foreach (var attitude in snapshot.Attitude)
            {
                rows.Add(new($"attitude.{attitude.VehicleId}", $"Attitude {attitude.VehicleId}", attitude.Mode,
                    $"set {attitude.ControlSet}, total {Number(attitude.TotalDps)} °/s, duty {Number(attitude.FiringDuty)}"));
            }
        }

        foreach (var (key, label, truthPath, factId, unit) in Readings(vehicle))
        {
            rows.Add(truth
                ? new(key, label, $"{Truth(state, truthPath)} {unit}", truthPath)
                : Known(key, label, facts.GetValueOrDefault(factId), unit));
        }
        if (truth) rows.Add(new("cabin.ppo2", "Cabin ppO2", $"{Number(snapshot.Resources.CabinPpO2Mmhg)} mmHg", "no fact (truth only)"));

        foreach (var crew in snapshot.Crew)
        {
            var key = $"crew.{crew.Id.Value}";
            rows.Add(truth
                ? new(key, crew.Name, $"{Number(crew.Health)} ({crew.Band})", $"{crew.Location}{(crew.Activity.Length > 0 ? $", {crew.Activity}" : "")}{(crew.Alive ? "" : ", deceased")}")
                : Known(key, crew.Name, facts.GetValueOrDefault($"crew.{crew.Id.Value}.condition"), ""));
        }

        rows.Add(snapshot.Task is { } task
            ? new("task", "Active task", task.Task.Value, $"{task.Assignee.Value}, {task.Progress:P0}, ends {task.EndsAt}")
            : new("task", "Active task", "none", ""));

        foreach (var objective in snapshot.Objectives)
        {
            rows.Add(new($"objective.{objective.Id.Value}", $"Objective {objective.Id.Value}", objective.State,
                $"{objective.Title}{(objective.Cause.Length > 0 ? $" ({objective.Cause})" : "")}"));
        }

        foreach (var alarm in snapshot.Alarms)
        {
            rows.Add(new($"alarm.{alarm.AlarmId}", $"Alarm {alarm.AlarmId}", alarm.Level.ToString(),
                $"{alarm.Subject}{(alarm.Value is { } value ? $" = {Number(value)}" : "")}, since {alarm.RaisedAt}{(alarm.Acknowledged ? ", acknowledged" : "")}"));
        }
        if (snapshot.Alarms.Count == 0) rows.Add(new("alarm.none", "Alarms", "none", ""));
        return rows;
    }

    public static string Truth(IStateQuery state, string path)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.TryGet(path, out var value)) return "n/a";
        return value.Kind switch
        {
            StateValueKind.Number => Number(value.AsNumber()),
            StateValueKind.Bool => value.AsBool() ? "true" : "false",
            StateValueKind.Text => value.AsText(),
            _ => value.ToString() ?? "",
        };
    }

    public static string KnownValue(FactView fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (fact.Value is { } number) return fact.Text is { Length: > 0 } text ? $"{Number(number)} ({text})" : Number(number);
        return fact.Text ?? "";
    }

    public static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static HarnessRow Known(string key, string label, FactView? fact, string unit)
    {
        if (fact is null) return new(key, label, "no fact", "");
        var value = KnownValue(fact);
        var reading = value.Length == 0 ? "—" : unit.Length == 0 ? value : $"{value} {unit}";
        var when = fact.ObservedAt is { } at ? $" @ {at}" : "";
        var band = fact.GroundBand is { Length: > 0 } groundBand ? $", ground: {groundBand}" : "";
        return new(key, label, reading, $"{fact.State}{when}{band}");
    }
}
