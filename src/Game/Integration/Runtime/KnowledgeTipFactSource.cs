using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Usa.Game.Guidance;
using Usa.Game.Integration.Adapters;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.Knowledge.Catalog;

namespace Usa.Game.Integration.Runtime;

/// <summary>
/// <see cref="ITipFactSource"/> over the console's knowledge readouts: the same chip wording as the console
/// (<c>OAMS propellant 212 kg · 06:58</c> once a value is known), and the ground's qualitative band for the rephraser.
/// Never reads simulation truth.
/// </summary>
public sealed class KnowledgeTipFactSource(FactCatalog catalog, KnowledgeBase knowledge, KnowledgeReadouts readouts) : ITipFactSource
{
    private readonly FactCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly KnowledgeBase _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
    private readonly KnowledgeReadouts _readouts = readouts ?? throw new ArgumentNullException(nameof(readouts));

    public bool TryDescribe(FactId fact, MissionTime now, [NotNullWhen(true)] out TipFactInfo? info)
    {
        if (!_catalog.TryResolve(fact, out var definition))
        {
            info = null;
            return false;
        }
        var readout = _readouts.Read(fact, now);
        var band = readout.GroundBand ?? _knowledge.Describe(fact).Latest?.Qualitative;
        info = new TipFactInfo(definition.Label, ChipLabel(definition.Label, definition.Unit, readout), readout.Knowledge,
            string.IsNullOrWhiteSpace(band) ? null : band);
        return true;
    }

    /// <summary>Matches the console adapter's chips, so a rephrased tip shows the same chips as the authored one.</summary>
    public static string ChipLabel(string label, string unit, ReadoutSnapshot readout)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(readout);
        if (readout.Value is not { } value || readout.ObservedAt is not { } at) return label;
        var minutes = at.Milliseconds / 60_000;
        return string.Create(CultureInfo.InvariantCulture, $"{label} {value:0.#} {unit} · {minutes / 60:00}:{minutes % 60:00}")
            .Replace("  ", " ", StringComparison.Ordinal);
    }
}
