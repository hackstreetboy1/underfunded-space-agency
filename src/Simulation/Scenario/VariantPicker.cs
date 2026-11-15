using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;
using Usa.Simulation.Scenario.Model;

namespace Usa.Simulation.Scenario;

public sealed record VariantPick(string VariantId, string Option);

/// <summary>
/// Picks one option per variant at mission start from the <c>variants</c> stream and serves them as
/// <c>variant.&lt;id&gt;</c> (text). Variants are drawn in id order, one draw each even when overridden,
/// so an override never shifts another variant's pick.
/// </summary>
public sealed class VariantPicker : IStateProvider, IHashContributor
{
    public const string RngStream = "variants";

    private readonly VariantPick[] _picks;
    private readonly Dictionary<string, string> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StateValue> _byPath = new(StringComparer.Ordinal);

    /// <param name="overrides">Forced picks by variant id (debug builds, tests, authored replays).</param>
    public VariantPicker(IReadOnlyList<Variant> variants, ulong scenarioSeed, IReadOnlyDictionary<string, string>? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(variants);
        var sorted = variants.ToArray();
        Array.Sort(sorted, (a, b) => string.CompareOrdinal(a.Id, b.Id));

        var rng = Pcg32.ForStream(scenarioSeed, RngStream);
        _picks = new VariantPick[sorted.Length];
        for (var i = 0; i < sorted.Length; i++)
        {
            var variant = sorted[i];
            var option = variant.Options[rng.NextInt(variant.Options.Count)];
            if (overrides is not null && overrides.TryGetValue(variant.Id, out var forced))
            {
                option = variant.Options.Contains(forced, StringComparer.Ordinal)
                    ? forced
                    : throw new ArgumentException($"Variant '{variant.Id}' has no option '{forced}'.", nameof(overrides));
            }
            _picks[i] = new VariantPick(variant.Id, option);
            _byId.Add(variant.Id, option);
            _byPath.Add(StatePaths.Variant + variant.Id, StateValue.Of(option));
        }

        if (overrides is not null)
        {
            foreach (var key in overrides.Keys)
            {
                if (!_byId.ContainsKey(key)) throw new ArgumentException($"Unknown variant '{key}'.", nameof(overrides));
            }
        }
    }

    public string Prefix => StatePaths.Variant;

    /// <summary>Picks in variant id order.</summary>
    public IReadOnlyList<VariantPick> Picks => _picks;

    /// <exception cref="ArgumentException">Unknown variant.</exception>
    public string Get(string variantId) =>
        _byId.TryGetValue(variantId, out var option) ? option : throw new ArgumentException($"Unknown variant '{variantId}'.", nameof(variantId));

    public bool IsPicked(string variantId, string option) =>
        _byId.TryGetValue(variantId, out var picked) && string.Equals(picked, option, StringComparison.Ordinal);

    public bool TryGet(string path, out StateValue value) => _byPath.TryGetValue(path, out value);

    public bool IsKnownPath(string path) => _byPath.ContainsKey(path);

    public void WriteHash(StateHashWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(_picks.Length);
        foreach (var pick in _picks)
        {
            writer.Write(pick.VariantId);
            writer.Write(pick.Option);
        }
    }
}
