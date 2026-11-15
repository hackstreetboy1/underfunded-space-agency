using System.Globalization;
using Usa.Simulation.Data;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Vehicle.Definitions;

/// <summary>Checks a deserialized vehicle for internal consistency and reports every problem it finds.</summary>
public static class VehicleDefinitionValidator
{
    private const double UnitVectorTolerance = 1e-3;

    public static void Validate(VehicleDefinition vehicle, string file, DefinitionErrors errors)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(errors);
        new Run(vehicle, file, errors).Execute();
    }

    /// <summary>Checks docking partners across vehicles: each must exist and point back.</summary>
    public static void ValidateDockingPartners(IReadOnlyList<(VehicleDefinition Vehicle, string File)> vehicles, DefinitionErrors errors)
    {
        ArgumentNullException.ThrowIfNull(vehicles);
        ArgumentNullException.ThrowIfNull(errors);

        var all = new Dictionary<ComponentId, ComponentDefinition>();
        foreach (var (vehicle, _) in vehicles)
        {
            foreach (var component in vehicle.Components) all.TryAdd(component.Id, component);
        }

        foreach (var (vehicle, file) in vehicles)
        {
            for (var i = 0; i < vehicle.Components.Count; i++)
            {
                var docking = vehicle.Components[i].Docking;
                if (docking is null) continue;
                var path = $"$.components[{i}].docking.partner";
                if (!all.TryGetValue(docking.Partner, out var partner))
                    errors.Add(file, path, $"Docking partner '{docking.Partner}' is not in any loaded vehicle.");
                else if (partner.Docking is null || partner.Docking.Partner != vehicle.Components[i].Id)
                    errors.Add(file, path, $"Docking partner '{docking.Partner}' does not name '{vehicle.Components[i].Id}' back.");
                else if (docking.Partner.Value.StartsWith(vehicle.Id + "/", StringComparison.Ordinal))
                    errors.Add(file, path, "A docking partner must be on another vehicle.");
            }
        }
    }

    private sealed class Run(VehicleDefinition vehicle, string file, DefinitionErrors errors)
    {
        private readonly Dictionary<ComponentId, ComponentDefinition> _components = [];
        private readonly HashSet<string> _sections = new(StringComparer.Ordinal);
        private readonly HashSet<ComponentId> _fedTargets = [];

        public void Execute()
        {
            if (string.IsNullOrWhiteSpace(vehicle.Id)) Error("$.id", "Vehicle id is empty.");
            if (string.IsNullOrWhiteSpace(vehicle.DisplayName)) Error("$.displayName", "Display name is empty.");
            ValidateSections();
            IndexComponents();
            for (var i = 0; i < vehicle.Components.Count; i++) ValidateComponent(vehicle.Components[i], $"$.components[{i}]");
            for (var i = 0; i < vehicle.Components.Count; i++) ValidateLoadFeed(vehicle.Components[i], $"$.components[{i}]");
            ValidateEnvironment();
        }

        private void ValidateSections()
        {
            if (vehicle.Sections.Count == 0) Error("$.sections", "A vehicle needs at least one section.");
            var jettisonEvents = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < vehicle.Sections.Count; i++)
            {
                var section = vehicle.Sections[i];
                var path = $"$.sections[{i}]";
                if (!_sections.Add(section.Id)) Error(path + ".id", $"Duplicate section id '{section.Id}'.");
                if (section.DryMassKg <= 0) Error(path + ".dryMassKg", "Dry mass must be positive.");
                Vector(section.CenterOfMassM, path + ".centerOfMassM");
                if (section.InertiaKgM2.Roll <= 0 || section.InertiaKgM2.Pitch <= 0 || section.InertiaKgM2.Yaw <= 0)
                    Error(path + ".inertiaKgM2", "Moments of inertia must be positive.");
                if (section.Jettison is { } jettison && !jettisonEvents.Add(jettison))
                    Error(path + ".jettison", $"Jettison event '{jettison}' is used by more than one section.");
            }
        }

        private void IndexComponents()
        {
            var prefix = vehicle.Id + "/";
            for (var i = 0; i < vehicle.Components.Count; i++)
            {
                var component = vehicle.Components[i];
                var path = $"$.components[{i}]";
                if (!_components.TryAdd(component.Id, component)) Error(path + ".id", $"Duplicate component id '{component.Id}'.");
                if (!component.Id.Value.StartsWith(prefix, StringComparison.Ordinal))
                    Error(path + ".id", $"Component id '{component.Id}' must start with '{prefix}'.");
                if (component.Id.Value.Contains('.', StringComparison.Ordinal))
                    Error(path + ".id", "Component ids must not contain '.', which separates the field in state paths.");

                var feed = component.Breaker ?? component.Switch;
                if (feed?.To is { } to) _fedTargets.Add(to);
                if (component.Harness is { } harness) _fedTargets.Add(harness.To);
            }
        }

        private void ValidateComponent(ComponentDefinition c, string path)
        {
            if (string.IsNullOrWhiteSpace(c.Name)) Error(path + ".name", "Name is empty.");
            if (!_sections.Contains(c.Section)) Error(path + ".section", $"Unknown section '{c.Section}'.");
            ValidateSpecPresence(c, path);

            for (var i = 0; i < c.Sensors.Count; i++)
                Ref(c.Sensors[i], $"{path}.sensors[{i}]", ComponentFamily.Sensor);

            if (c.Load is { } load) ValidateLoad(load, path + ".load");
            if (c.Thruster is { } thruster) ValidateThruster(thruster, path + ".thruster");
            if (c.Engine is { } engine) ValidateThruster(engine, path + ".engine");
            if (c.ThrusterGroup is { } group)
            {
                if (group.LowActivityFraction < 0 || group.HighActivityFraction <= group.LowActivityFraction || group.HighActivityFraction > 1)
                    Error(path + ".thrusterGroup", "Activity fractions need 0 <= low < high <= 1.");
            }
            if (c.Tank is { } tank) ValidateTank(tank, path + ".tank");
            if (c.Breaker is { } breaker) ValidateSwitch(c, breaker, path + ".breaker");
            if (c.Switch is { } sw) ValidateSwitch(c, sw, path + ".switch");
            if (c.Bus is { } bus)
            {
                if (bus.NominalV <= 0 || bus.BrownoutV <= 0 || bus.DroopV < 0 || bus.NominalV - bus.DroopV <= bus.BrownoutV)
                    Error(path + ".bus", "Bus needs 0 < brownoutV < nominalV - droopV and droopV >= 0.");
            }
            if (c.FuelCell is { } cell) ValidateFuelCell(cell, path + ".fuelCell");
            if (c.Battery is { } battery) ValidateBattery(battery, path + ".battery");
            if (c.Sensor is { } sensor) ValidateSensor(sensor, path + ".sensor");
            if (c.Harness is { } harness)
            {
                Ref(harness.From, path + ".harness.from", null);
                Ref(harness.To, path + ".harness.to", null);
                if (harness.From == harness.To) Error(path + ".harness", "A harness cannot connect a component to itself.");
            }
            if (c.Outlet is { } outlet) Ref(outlet.Bus, path + ".outlet.bus", ComponentFamily.Bus);
            if (c.Docking is { } docking)
            {
                if (docking.FullLoadRollRateDps <= 0 || docking.AlarmRollRateDps <= 0 || docking.DamageAfterS <= 0)
                    Error(path + ".docking", "Docking thresholds must be positive.");
            }
            if (c.Parachute is { } chute)
            {
                if (chute.DragAreaM2 <= 0 || chute.DragCoefficient <= 0 || chute.NominalDeployAltitudeKm <= 0)
                    Error(path + ".parachute", "Parachute values must be positive.");
            }
            if (c.RetroRocket is { } retro)
            {
                if (retro.ThrustN <= 0 || retro.BurnS <= 0 || retro.PropellantKg < 0)
                    Error(path + ".retroRocket", "Retro rocket needs positive thrust and burn time.");
                if (!(double.IsFinite(retro.IgnitionOffsetS) && retro.IgnitionOffsetS >= 0))
                    Error(path + ".retroRocket.ignitionOffsetS", "Ignition offset must be finite and not negative.");
                Vector(retro.PositionM, path + ".retroRocket.positionM");
                UnitVector(retro.DirectionUnit, path + ".retroRocket.directionUnit");
            }
            if (c.ThermalNode is { } node)
            {
                if (node.HeatCapacityJPerK <= 0) Error(path + ".thermalNode.heatCapacityJPerK", "Heat capacity must be positive.");
                if (node.MinC >= node.MaxC) Error(path + ".thermalNode", "minC must be below maxC.");
            }
            if (c.Heater is { } heater)
            {
                Ref(heater.Node, path + ".heater.node", ComponentFamily.ThermalNode);
                if (heater.HysteresisC < 0) Error(path + ".heater.hysteresisC", "Hysteresis cannot be negative.");
            }
            if (c.Radiator is { } radiator)
            {
                Ref(radiator.Node, path + ".radiator.node", ComponentFamily.ThermalNode);
                if (radiator.AreaM2 <= 0 || radiator.Emissivity is < 0 or > 1 || radiator.Absorptivity is < 0 or > 1)
                    Error(path + ".radiator", "Radiator needs a positive area and emissivity/absorptivity in [0, 1].");
            }
            if (c.Regulator is { } regulator)
            {
                Ref(regulator.Tank, path + ".regulator.tank", ComponentFamily.Tank);
                if (regulator.SecondaryTank is { } secondary) Ref(secondary, path + ".regulator.secondaryTank", ComponentFamily.Tank);
                if (regulator.SetpointKpa <= 0 || regulator.MaxFlowKgPerS <= 0) Error(path + ".regulator", "Regulator values must be positive.");
            }
            if (c.Scrubber is { } scrubber && (scrubber.CapacityKgCo2 <= 0 || scrubber.RemovalEfficiency is <= 0 or > 1))
                Error(path + ".scrubber", "Scrubber needs a positive capacity and efficiency in (0, 1].");
            if (c.Pump is { FlowKgPerS: <= 0 }) Error(path + ".pump.flowKgPerS", "Flow must be positive.");
            if (c.Fan is { AirflowM3PerS: <= 0 }) Error(path + ".fan.airflowM3PerS", "Airflow must be positive.");
            if (c.Payload is { MassKg: < 0 }) Error(path + ".payload.massKg", "Mass cannot be negative.");
            if (c.HeatShield is { AblatorKg: < 0 }) Error(path + ".heatShield.ablatorKg", "Ablator mass cannot be negative.");
        }

        private void ValidateSpecPresence(ComponentDefinition c, string path)
        {
            Spec(c, path, "thruster", c.Thruster, ComponentFamily.Thruster, required: true);
            Spec(c, path, "engine", c.Engine, ComponentFamily.Engine, required: true);
            Spec(c, path, "thrusterGroup", c.ThrusterGroup, ComponentFamily.ThrusterGroup, required: true);
            Spec(c, path, "tank", c.Tank, ComponentFamily.Tank, required: true);
            Spec(c, path, "breaker", c.Breaker, ComponentFamily.Breaker, required: true);
            Spec(c, path, "switch", c.Switch, ComponentFamily.Switch, required: true);
            Spec(c, path, "bus", c.Bus, ComponentFamily.Bus, required: true);
            Spec(c, path, "fuelCell", c.FuelCell, ComponentFamily.FuelCell, required: true);
            Spec(c, path, "battery", c.Battery, ComponentFamily.Battery, required: true);
            Spec(c, path, "sensor", c.Sensor, ComponentFamily.Sensor, required: true);
            Spec(c, path, "harness", c.Harness, ComponentFamily.Harness, required: true);
            Spec(c, path, "outlet", c.Outlet, ComponentFamily.Outlet, required: true);
            Spec(c, path, "docking", c.Docking, ComponentFamily.Docking, required: true);
            Spec(c, path, "parachute", c.Parachute, ComponentFamily.Parachute, required: true);
            Spec(c, path, "retroRocket", c.RetroRocket, ComponentFamily.RetroRocket, required: true);
            Spec(c, path, "thermalNode", c.ThermalNode, ComponentFamily.ThermalNode, required: true);
            Spec(c, path, "heater", c.Heater, ComponentFamily.Heater, required: true);
            Spec(c, path, "radiator", c.Radiator, ComponentFamily.Radiator, required: true);
            Spec(c, path, "pump", c.Pump, ComponentFamily.Pump, required: true);
            Spec(c, path, "fan", c.Fan, ComponentFamily.Fan, required: true);
            Spec(c, path, "scrubber", c.Scrubber, ComponentFamily.Scrubber, required: true);
            Spec(c, path, "regulator", c.Regulator, ComponentFamily.Regulator, required: true);
            Spec(c, path, "valve", c.Valve, ComponentFamily.Valve, required: false);
            Spec(c, path, "heatShield", c.HeatShield, ComponentFamily.HeatShield, required: false);
            Spec(c, path, "payload", c.Payload, ComponentFamily.Payload, required: false);

            if (c.Load is null && c.Family is ComponentFamily.Load or ComponentFamily.Driver or ComponentFamily.Heater)
                Error(path, $"Family '{Name(c.Family)}' needs a 'load' object.");
        }

        private void Spec(ComponentDefinition c, string path, string key, object? spec, ComponentFamily family, bool required)
        {
            if (spec is not null && c.Family != family)
                Error($"{path}.{key}", $"'{key}' is only valid on family '{Name(family)}', not '{Name(c.Family)}'.");
            else if (spec is null && required && c.Family == family)
                Error(path, $"Family '{Name(family)}' needs a '{key}' object.");
        }

        private void ValidateLoad(LoadSpec load, string path)
        {
            if (load.PowerW < 0) Error(path + ".powerW", "Power cannot be negative.");
            if (load.HeatW is < 0) Error(path + ".heatW", "Heat cannot be negative.");
            if (load.Bus is { } bus) Ref(bus, path + ".bus", ComponentFamily.Bus);
            if (load.ThermalNode is { } node) Ref(node, path + ".thermalNode", ComponentFamily.ThermalNode);
        }

        private void ValidateLoadFeed(ComponentDefinition c, string path)
        {
            if (c.Load is { Bus: null } && !_fedTargets.Contains(c.Id))
                Error(path + ".load", "A load without 'bus' must be the 'to' of a switch, breaker or harness.");
        }

        private void ValidateThruster(ThrusterSpec thruster, string path)
        {
            if (thruster.ThrustN <= 0) Error(path + ".thrustN", "Thrust must be positive.");
            if (thruster.IspS <= 0) Error(path + ".ispS", "Isp must be positive.");
            if (thruster.HotC < thruster.ColdC) Error(path + ".hotC", "hotC must not be below coldC.");
            if (thruster.ThermalTimeConstantS <= 0) Error(path + ".thermalTimeConstantS", "Time constant must be positive.");
            Vector(thruster.PositionM, path + ".positionM");
            UnitVector(thruster.DirectionUnit, path + ".directionUnit");
            Ref(thruster.Tank, path + ".tank", ComponentFamily.Tank);
            if (thruster.Group is { } group) Ref(group, path + ".group", ComponentFamily.ThrusterGroup);
            if (thruster.Driver is { } driver && Ref(driver, path + ".driver", null) is { Load: null })
                Error(path + ".driver", $"Driver '{driver}' has no 'load', so it can never be powered.");
        }

        private void ValidateTank(TankSpec tank, string path)
        {
            if (string.IsNullOrWhiteSpace(tank.Resource)) Error(path + ".resource", "Resource is empty.");
            if (tank.CapacityKg <= 0) Error(path + ".capacityKg", "Capacity must be positive.");
            if (tank.InitialKg < 0 || tank.InitialKg > tank.CapacityKg) Error(path + ".initialKg", "Initial quantity must be within [0, capacityKg].");
            Vector(tank.PositionM, path + ".positionM");
        }

        private void ValidateSwitch(ComponentDefinition owner, SwitchSpec sw, string path)
        {
            if (Ref(sw.From, path + ".from", null) is { } from &&
                from.Family is not (ComponentFamily.Bus or ComponentFamily.Switch or ComponentFamily.Breaker or ComponentFamily.Outlet))
                Error(path + ".from", $"'{sw.From}' must be a bus, outlet, switch or breaker.");
            if (sw.To is { } to)
            {
                Ref(to, path + ".to", null);
                if (to == owner.Id || to == sw.From) Error(path + ".to", "A switch must connect two different components.");
            }
        }

        private void ValidateFuelCell(FuelCellSpec cell, string path)
        {
            Ref(cell.Bus, path + ".bus", ComponentFamily.Bus);
            TankOf(cell.O2Tank, "o2", path + ".o2Tank");
            TankOf(cell.H2Tank, "h2", path + ".h2Tank");
            TankOf(cell.WaterTank, "water", path + ".waterTank");
            if (cell.ThermalNode is { } node) Ref(node, path + ".thermalNode", ComponentFamily.ThermalNode);
            if (cell.MaxPowerW <= 0) Error(path + ".maxPowerW", "Power must be positive.");
            if (cell.ReactantKgPerKwh <= 0) Error(path + ".reactantKgPerKwh", "Reactant use must be positive.");
            if (cell.WasteHeatFraction < 0) Error(path + ".wasteHeatFraction", "Waste heat cannot be negative.");
        }

        private void ValidateBattery(BatterySpec battery, string path)
        {
            Ref(battery.Bus, path + ".bus", ComponentFamily.Bus);
            if (battery.ThermalNode is { } node) Ref(node, path + ".thermalNode", ComponentFamily.ThermalNode);
            if (battery.CapacityKwh <= 0) Error(path + ".capacityKwh", "Capacity must be positive.");
            if (battery.InitialKwh < 0 || battery.InitialKwh > battery.CapacityKwh) Error(path + ".initialKwh", "Initial energy must be within [0, capacityKwh].");
            if (battery.MaxDischargeW <= 0 || battery.MaxChargeW < 0) Error(path, "Needs maxDischargeW > 0 and maxChargeW >= 0.");
        }

        private void ValidateSensor(SensorSpec sensor, string path)
        {
            if (sensor.Channels.Count == 0) Error(path + ".channels", "A sensor needs at least one channel.");
            if (sensor.NoiseStd < 0 || sensor.Quantization < 0 || sensor.DegradedNoiseFactor < 1)
                Error(path, "Needs noiseStd >= 0, quantization >= 0 and degradedNoiseFactor >= 1.");

            var fields = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < sensor.Channels.Count; i++)
            {
                var channel = sensor.Channels[i];
                var channelPath = $"{path}.channels[{i}]";
                if (!fields.Add(channel.Field)) Error(channelPath + ".field", $"Duplicate channel field '{channel.Field}'.");
                if (ComponentFields.IsReserved(channel.Field)) Error(channelPath + ".field", $"'{channel.Field}' is a built-in component field.");
                var dot = channel.Measures.LastIndexOf('.');
                if (dot <= 0 || dot == channel.Measures.Length - 1 || channel.Measures.StartsWith('@'))
                {
                    Error(channelPath + ".measures", $"'{channel.Measures}' is not a state path (owner.field, without '@').");
                    continue;
                }
                var owner = channel.Measures[..dot];
                if (owner.StartsWith(vehicle.Id + "/", StringComparison.Ordinal) && !_components.ContainsKey(new ComponentId(owner)))
                    Error(channelPath + ".measures", $"Unknown component '{owner}'.");
            }
        }

        private void ValidateEnvironment()
        {
            for (var i = 0; i < vehicle.ThermalLinks.Count; i++)
            {
                var link = vehicle.ThermalLinks[i];
                var path = $"$.thermalLinks[{i}]";
                Ref(link.A, path + ".a", ComponentFamily.ThermalNode);
                Ref(link.B, path + ".b", ComponentFamily.ThermalNode);
                if (link.A == link.B) Error(path, "A thermal link needs two different nodes.");
                if (link.ConductanceWPerK <= 0) Error(path + ".conductanceWPerK", "Conductance must be positive.");
            }

            if (vehicle.Cabin is { } cabin)
            {
                Ref(cabin.Compartment, "$.cabin.compartment", ComponentFamily.Compartment);
                Ref(cabin.ThermalNode, "$.cabin.thermalNode", ComponentFamily.ThermalNode);
                if (cabin.VolumeM3 <= 0 || cabin.InitialPressureKpa <= 0 || cabin.InitialPpO2Kpa <= 0 ||
                    cabin.InitialPpCo2Kpa < 0 || cabin.LeakRateKgPerS < 0)
                    Error("$.cabin", "Cabin needs positive volume, pressure and ppO2, and non-negative ppCO2 and leak rate.");
                if (cabin.InitialPpO2Kpa + cabin.InitialPpCo2Kpa > cabin.InitialPressureKpa + 1e-9)
                    Error("$.cabin", "Partial pressures exceed the total pressure.");
            }

            if (vehicle.Provisions is { } provisions && (provisions.FoodKcal < 0 || provisions.PotableWaterL < 0))
                Error("$.provisions", "Provisions cannot be negative.");
        }

        private void TankOf(ComponentId id, string resource, string path)
        {
            if (Ref(id, path, ComponentFamily.Tank) is { Tank: { } tank } && !string.Equals(tank.Resource, resource, StringComparison.Ordinal))
                Error(path, $"Tank '{id}' holds '{tank.Resource}', expected '{resource}'.");
        }

        private ComponentDefinition? Ref(ComponentId id, string path, ComponentFamily? family)
        {
            if (!_components.TryGetValue(id, out var target))
            {
                Error(path, $"Unknown component '{id}'.");
                return null;
            }
            if (family is { } expected && target.Family != expected)
            {
                Error(path, $"'{id}' is a '{Name(target.Family)}', expected '{Name(expected)}'.");
                return null;
            }
            return target;
        }

        private void Vector(IReadOnlyList<double> vector, string path)
        {
            if (vector.Count != 3) Error(path, "Expected [x, y, z].");
        }

        private void UnitVector(IReadOnlyList<double> vector, string path)
        {
            if (vector.Count != 3)
            {
                Error(path, "Expected [x, y, z].");
                return;
            }
            var length = Math.Sqrt(vector[0] * vector[0] + vector[1] * vector[1] + vector[2] * vector[2]);
            if (Math.Abs(length - 1) > UnitVectorTolerance)
                Error(path, $"Expected a unit vector, length is {length.ToString("0.####", CultureInfo.InvariantCulture)}.");
        }

        private static string Name(ComponentFamily family) => ComponentFields.FamilyName(family);

        private void Error(string path, string message) => errors.Add(file, path, message);
    }
}
