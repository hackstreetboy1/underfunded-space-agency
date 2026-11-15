using Usa.Game.Content;
using Usa.Game.Flow.Data;

namespace Usa.Game.UI.Frontend.Model;

/// <param name="CropTopFraction">Share of the image height cut from the top (the printed catalogue label must not show).</param>
public sealed record EncyclopediaImage(string LocalPath, string Credit, string? Caption, double CropTopFraction);

/// <summary>
/// An entry's curated image from its <see cref="EncyclopediaEntry.ImageKey"/> (the approved Gate 6 NASA package,
/// <c>docs/review/gate-6/nasa/README.md</c>). Only shipped manifest items are shown.
/// </summary>
public static class EncyclopediaImages
{
    /// <summary>The two 1964 diagrams print "NASA-S-64-…" top left; the README asks to crop it.</summary>
    public const double CatalogueLabelCrop = 0.1;

    /// <summary>Images that carry a printed catalogue label (a property of the picture, not of the entry).</summary>
    private static readonly HashSet<string> LabelledImages = new(StringComparer.Ordinal)
    {
        "image_library:s64-03506",
        "image_library:s64-03507",
    };

    public static EncyclopediaImage? For(EncyclopediaEntry entry, NasaManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(manifest);
        if (entry.ImageKey is not { } key) return null;
        var item = manifest.Items.FirstOrDefault(i => i.Shipped && string.Equals(i.Key, key, StringComparison.Ordinal));
        if (item is null) return null;
        return new EncyclopediaImage(item.LocalPath, $"{item.Credit} · {item.ItemId}", item.DisplayCaption,
            LabelledImages.Contains(key) ? CatalogueLabelCrop : 0);
    }
}
