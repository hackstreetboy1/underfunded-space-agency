using Usa.Game.Content;
using Usa.Game.Flow.Data;

namespace Usa.Game.Flow.Screens;

public sealed record CreditsSection(string Title, IReadOnlyList<string> Lines);

/// <summary>One shipped NASA item's source credit (GDD 16: every item credited).</summary>
public sealed record NasaCreditLine(string Key, string Credit, string Dataset, string ItemId, string SourceUrl, string Licence)
{
    public string Text => $"{Credit} · {Dataset} · {ItemId}";
}

/// <summary>
/// Credits (UI.md 5, GDD 16): team, tools, fonts (IBM Plex, OFL), NASA source credits per manifest item, the
/// hackathon track line and the no-endorsement disclaimer.
/// </summary>
public sealed class CreditsModel
{
    public CreditsModel(StringTable strings, NasaManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(manifest);

        NasaItems = manifest.Shipped()
            .Select(i => new NasaCreditLine(i.Key, i.Credit, i.Dataset, i.ItemId, i.SourceUrl, i.Licence))
            .ToArray();
        ImageryCredit = strings.Get("credits.imageryCredit");
        HackathonLine = strings.Get("credits.hackathonLine");
        Disclaimer = strings.Get("credits.disclaimer");
        Fonts = strings.Get("credits.fonts");

        var imagery = new List<string> { ImageryCredit };
        imagery.AddRange(NasaItems.Select(i => i.Text));

        Sections =
        [
            new CreditsSection(strings.Get("credits.teamTitle"), [strings.Get("credits.team")]),
            new CreditsSection(
                strings.Get("credits.toolsTitle"),
                [
                    strings.Get("credits.toolEngine"),
                    strings.Get("credits.toolModeling"),
                    strings.Get("credits.toolDesign"),
                    strings.Get("credits.toolSpeech"),
                    strings.Get("credits.toolLanguage"),
                ]),
            new CreditsSection(strings.Get("credits.fontsTitle"), [Fonts]),
            new CreditsSection(strings.Get("credits.imageryTitle"), imagery),
            new CreditsSection(strings.Get("credits.supportTitle"), [HackathonLine, Disclaimer]),
        ];
    }

    public IReadOnlyList<CreditsSection> Sections { get; }

    /// <summary>Empty while the manifest has no shipped items; <see cref="ImageryCredit"/> still shows.</summary>
    public IReadOnlyList<NasaCreditLine> NasaItems { get; }

    public string ImageryCredit { get; }

    /// <summary>"Supported by the Michigan Space Grant Consortium, NASA grant 80NSSC20M0124".</summary>
    public string HackathonLine { get; }

    /// <summary>"Not affiliated with or endorsed by NASA."</summary>
    public string Disclaimer { get; }

    /// <summary>IBM Plex Sans and Mono, SIL Open Font License.</summary>
    public string Fonts { get; }
}
