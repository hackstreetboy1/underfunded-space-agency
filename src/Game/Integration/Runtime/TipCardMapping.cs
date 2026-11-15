using Usa.Game.Guidance;
using Usa.Game.Integration.Adapters;
using Usa.Game.Presentation.Console.Snapshots;
using Usa.Simulation.Kernel;

namespace Usa.Game.Integration.Runtime;

/// <summary><see cref="TipService"/>'s card (authored or validated rephrasing) as the console's tip snapshot.</summary>
public static class TipCardMapping
{
    /// <param name="layout">Gives each chip the systems-board tab its fact belongs to.</param>
    public static GuidanceTipSnapshot? ToSnapshot(TipCardInput? card, ConsoleLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (card is null) return null;
        var facts = new TipFactSnapshot[card.Facts.Count];
        for (var i = 0; i < facts.Length; i++)
        {
            var chip = card.Facts[i];
            facts[i] = new TipFactSnapshot(chip.Label)
            {
                FactKey = chip.FactKey,
                Tab = layout.GroupOf(new FactId(chip.FactKey))?.Tab,
                Knowledge = chip.Knowledge,
            };
        }
        return new GuidanceTipSnapshot(card.TipId, AdapterMaps.Stage(card.Stage), card.Text, card.IssuedAt) { Facts = facts };
    }
}
