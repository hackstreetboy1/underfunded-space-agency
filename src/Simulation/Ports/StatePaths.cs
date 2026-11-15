namespace Usa.Simulation.Ports;

/// <summary>
/// State path conventions shared by every area. A path is "&lt;owner prefix&gt;...&lt;field&gt;".
/// Each area's <see cref="IStateProvider"/> serves its own prefix and documents its fields.
/// <list type="table">
/// <item><term>&lt;componentId&gt;.&lt;field&gt;</term><description>Vehicle area. Component ids start with the vehicle id,
/// e.g. <c>tandem/oams/thruster_08.valve_open</c>, <c>tandem/rcs/ring_a.armed</c>, <c>tandem/power/bus_a.voltage_v</c>.
/// Every component has <c>condition</c> ('nominal'|'degraded'|'failed'|'isolated') and <c>fault</c> (text, '' when none).</description></item>
/// <item><term>vehicle.&lt;vehicleId&gt;.&lt;field&gt;</term><description>Vehicle totals: <c>mass_kg</c>, <c>power_gen_w</c>, <c>power_load_w</c>.</description></item>
/// <item><term>attitude.&lt;vehicleId&gt;.&lt;field&gt;</term><description><c>rate_roll_dps</c>, <c>rate_pitch_dps</c>, <c>rate_yaw_dps</c>, <c>rate_total_dps</c>, <c>mode</c>.</description></item>
/// <item><term>flight.&lt;field&gt;</term><description><c>docked</c>, <c>altitude_km</c>, <c>periapsis_km</c>, <c>apoapsis_km</c>, <c>phase</c>, <c>g_load</c>, <c>landed</c>.</description></item>
/// <item><term>crew.&lt;crewId&gt;.&lt;field&gt;</term><description><c>health</c> (0-100), <c>alive</c>, <c>band</c>, <c>busy</c>.
/// Aggregates: <c>crew.count</c>, <c>crew.alive_count</c>, <c>crew.task.active</c>, <c>crew.task.id</c>.</description></item>
/// <item><term>knowledge.fact.&lt;factId&gt;.&lt;field&gt;</term><description><c>state</c> ('unknown'|'known'|'stale'|'conflicting'|'estimated'), <c>value</c>.</description></item>
/// <item><term>knowledge.component.&lt;componentId&gt;.state</term><description>'unknown'|'suspected'|'confirmed_nominal'|'confirmed_damaged'.</description></item>
/// <item><term>mission.&lt;field&gt;</term><description><c>met_s</c>, <c>phase</c>, <c>objective.&lt;id&gt;.state</c>,
/// <c>rule.&lt;id&gt;.active</c>, <c>event.&lt;eventId&gt;.fired</c>, <c>event.&lt;eventId&gt;.met_s</c>.</description></item>
/// <item><term>inventory.&lt;partId&gt;.count</term><description>Spare parts left.</description></item>
/// <item><term>difficulty.&lt;key&gt;</term><description>Values from the active difficulty profile.</description></item>
/// <item><term>variant.&lt;key&gt;</term><description>Scenario variant picks (text).</description></item>
/// </list>
/// In expressions, paths are written with an <c>@</c> prefix: <c>@tandem/rcs/ring_a.armed and @attitude.tandem.rate_roll_dps &gt; 120</c>.
/// </summary>
public static class StatePaths
{
    public const string Attitude = "attitude.";
    public const string Flight = "flight.";
    public const string Vehicle = "vehicle.";
    public const string Crew = "crew.";
    public const string KnowledgeFact = "knowledge.fact.";
    public const string KnowledgeComponent = "knowledge.component.";
    public const string Mission = "mission.";
    public const string Inventory = "inventory.";
    public const string Difficulty = "difficulty.";
    public const string Variant = "variant.";

    /// <summary>Splits "owner.part.field" at the last '.' into (owner path, field).</summary>
    public static (string Owner, string Field) SplitField(string path)
    {
        var dot = path.LastIndexOf('.');
        return dot <= 0 || dot == path.Length - 1
            ? throw new UnknownStatePathException(path)
            : (path[..dot], path[(dot + 1)..]);
    }
}
