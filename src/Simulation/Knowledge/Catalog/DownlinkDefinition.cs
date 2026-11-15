using Usa.Simulation.Ports;

namespace Usa.Simulation.Knowledge.Catalog;

/// <summary>Coarse ground telemetry for a fact. Bands are sorted by their lower bound.</summary>
public sealed record DownlinkDefinition(string TelemetryPath, IReadOnlyList<GroundBand> Bands, IReadOnlyList<GroundWord> Words)
{
    /// <summary>True when <paramref name="word"/> is one of this downlink's band or text words.</summary>
    public bool HasWord(string word)
    {
        for (var i = 0; i < Bands.Count; i++)
        {
            if (string.Equals(Bands[i].Word, word, StringComparison.Ordinal)) return true;
        }
        for (var i = 0; i < Words.Count; i++)
        {
            if (string.Equals(Words[i].Word, word, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>The ground word for a telemetry value, or null when the value maps to no band.</summary>
    public string? WordFor(StateValue telemetry)
    {
        switch (telemetry.Kind)
        {
            case StateValueKind.Number:
                var value = telemetry.AsNumber();
                if (double.IsNaN(value)) return null;
                for (var i = Bands.Count - 1; i >= 0; i--)
                {
                    var band = Bands[i];
                    if (band.From is not { } from || value >= from) return band.Word;
                }
                return null;
            case StateValueKind.Text:
                var text = telemetry.AsText();
                for (var i = 0; i < Words.Count; i++)
                {
                    if (string.Equals(Words[i].Value, text, StringComparison.Ordinal)) return Words[i].Word;
                }
                return null;
            default:
                return null;
        }
    }
}
