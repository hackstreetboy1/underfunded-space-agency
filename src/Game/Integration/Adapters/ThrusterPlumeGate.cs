using Usa.Game.Presentation.Spacecraft;
using Usa.Simulation.Ports;
using Usa.Simulation.Session;
using Usa.Simulation.Vehicle;
using Usa.Simulation.Vehicle.Definitions;

namespace Usa.Game.Integration.Adapters;

/// <summary>
/// The plume rule (GDD 4.4) for every channel besides the readouts that shows a thruster firing: the 3D plumes and the
/// thruster pulse sounds. Engines and retro rockets fire on the player's order and always show. An attitude thruster
/// shows nothing until the player has confirmed its condition. Confirmed damaged, it shows its true firing (the stuck
/// plume the inspection found). Confirmed nominal, it shows only the opening above its stuck fraction, i.e. what the
/// controller commanded: a confirmation made before a fault describes the thruster as it was, so the hidden opening the
/// fault added must not show.
/// </summary>
public sealed class ThrusterPlumeGate
{
    private const double StuckMarginFraction = 1e-6;
    private const string ConfirmedNominal = "confirmed_nominal";
    private const string ConfirmedDamaged = "confirmed_damaged";

    private readonly IStateQuery _state;
    private readonly Dictionary<string, int> _indexById = new(StringComparer.Ordinal);
    private readonly Entry[] _entries;
    private readonly string[] _ids;

    public ThrusterPlumeGate(MissionData data, IStateQuery state)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        var entries = new List<Entry>();
        foreach (var vehicle in data.Vehicles)
        {
            foreach (var component in vehicle.Components)
            {
                var id = component.Id.Value;
                Entry? entry = component.Family switch
                {
                    ComponentFamily.Thruster => new Entry(id, $"{id}.valve_open", Burning: false, Gated: true,
                        $"{id}.{ComponentFields.StuckFraction}", $"{StatePaths.KnowledgeComponent}{id}.state"),
                    ComponentFamily.Engine => new Entry(id, $"{id}.valve_open", Burning: false, Gated: false, null, null),
                    ComponentFamily.RetroRocket => new Entry(id, $"{id}.burning", Burning: true, Gated: false, null, null),
                    _ => null,
                };
                if (entry is null) continue;
                _indexById[id] = entries.Count;
                entries.Add(entry);
            }
        }
        _entries = [.. entries];
        _ids = [.. entries.Select(e => e.Id)];
    }

    /// <summary>Every thruster, engine and retro rocket of the mission, in data order.</summary>
    public IReadOnlyList<string> ComponentIds => _ids;

    /// <summary>True for attitude thrusters (the gated family); false for engines, retro rockets and unknown ids.</summary>
    public bool IsAttitudeThruster(int index) => _entries[index].Gated;

    /// <returns>The index for <see cref="VisibleFraction(int)"/>, or -1 when the id is not a thruster, engine or retro rocket.</returns>
    public int IndexOf(string componentId) =>
        componentId is not null && _indexById.TryGetValue(componentId, out var index) ? index : -1;

    /// <summary>The firing fraction 0..1 the player may see for the component at <paramref name="index"/>.</summary>
    public double VisibleFraction(int index)
    {
        var entry = _entries[index];
        double fraction;
        if (entry.Burning) fraction = Bool(entry.FiringPath) ? 1 : 0;
        else fraction = Math.Clamp(Number(entry.FiringPath), 0, 1);
        if (!entry.Gated || fraction <= 0) return fraction;

        if (!_state.TryGet(entry.KnowledgePath!, out var known) || known.Kind != StateValueKind.Text) return 0;
        var word = known.AsText();
        if (string.Equals(word, ConfirmedDamaged, StringComparison.Ordinal)) return fraction;
        if (!string.Equals(word, ConfirmedNominal, StringComparison.Ordinal)) return 0;
        return fraction > Number(entry.StuckPath!) + StuckMarginFraction ? fraction : 0;
    }

    public PlumeKind KindOf(string componentId)
    {
        if (IndexOf(componentId) is not (var index and >= 0)) return PlumeKind.None;
        var entry = _entries[index];
        return entry.Gated ? PlumeKind.AttitudeThruster : entry.Burning ? PlumeKind.RetroRocket : PlumeKind.Engine;
    }

    public double VisibleFraction(string componentId) => IndexOf(componentId) is var index and >= 0 ? VisibleFraction(index) : 0;

    private bool Bool(string path) => _state.TryGet(path, out var value) && value.Kind == StateValueKind.Bool && value.AsBool();

    private double Number(string path) => _state.TryGet(path, out var value) && value.Kind == StateValueKind.Number ? value.AsNumber() : 0;

    private sealed record Entry(string Id, string FiringPath, bool Burning, bool Gated, string? StuckPath, string? KnowledgePath);
}
