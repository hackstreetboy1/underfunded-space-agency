using Usa.Game.Content;
using Usa.Game.Flow.Data;

namespace Usa.Game.Flow.Screens;

/// <summary>The main-menu backdrop image with its required label (UI.md 5, GDD 16).</summary>
/// <param name="LocalPath">Cached media path relative to the project root (<c>res://</c>).</param>
/// <param name="Label">"Astronomy Picture of the Day · date · credit" for APOD items.</param>
/// <param name="ModernReferenceTag">"modern reference" for present-day imagery (always for APOD and EPIC).</param>
public sealed record MenuBackdrop(string SourceKey, string LocalPath, string Label, string? ModernReferenceTag, string? Caption)
{
    /// <summary>
    /// Rotates through the cached main-menu items by launch count (launch 1 shows the first item). Null when the
    /// manifest has none, so the menu shows its plain background.
    /// </summary>
    public static MenuBackdrop? Pick(NasaManifest manifest, int launchCount, StringTable strings)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(strings);
        var items = manifest.ForTouchpoint(NasaTouchpoints.MainMenu);
        if (items.Count == 0) return null;

        var index = (int)((uint)Math.Max(launchCount - 1, 0) % (uint)items.Count);
        var item = items[index];
        var label = item.Source == NasaSources.Apod
            ? strings.Format("mainMenu.apodLabel", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["date"] = item.ItemId,
                ["credit"] = item.Credit,
            })
            : $"{item.Dataset} · {item.Credit}";
        var tag = item.IsModernReference ? strings.Get("mainMenu.modernReferenceTag") : null;
        return new MenuBackdrop(item.Key, item.LocalPath, label, tag, item.DisplayCaption);
    }
}
