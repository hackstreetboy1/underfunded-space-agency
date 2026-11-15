using Usa.Game.Content;
using Usa.Simulation.Kernel;

namespace Usa.Game.Flow.Screens;

/// <summary>
/// Loading screen (UI.md 5): one agency humour line and one encyclopedia tip, picked from the launch seed so the same
/// run always shows the same text and a different variant usually shows another.
/// </summary>
public sealed class LoadingModel
{
    public const string LineStream = "frontend.loading_line";
    public const string TipStream = "frontend.loading_tip";

    /// <param name="seed">The launch seed; resumed runs pass the run number or any stable value.</param>
    public LoadingModel(StringTable strings, Encyclopedia encyclopedia, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(encyclopedia);

        var lines = strings.LoadingLines;
        Line = lines.Count == 0 ? "" : lines[Pcg32.ForStream(seed, LineStream).NextInt(lines.Count)];

        var entries = encyclopedia.Entries;
        TipPrefix = strings.Get("loadingTipPrefix");
        if (entries.Count > 0)
        {
            var entry = entries[Pcg32.ForStream(seed, TipStream).NextInt(entries.Count)];
            TipEntryId = entry.Id;
            TipTitle = entry.Title;
            TipText = entry.FirstUse;
        }
    }

    /// <summary>The agency humour line (UI.md 6.3: humour belongs in loading lines).</summary>
    public string Line { get; }

    /// <summary>"From the encyclopedia:".</summary>
    public string TipPrefix { get; }

    public string? TipEntryId { get; }

    public string? TipTitle { get; }

    public string? TipText { get; }
}
