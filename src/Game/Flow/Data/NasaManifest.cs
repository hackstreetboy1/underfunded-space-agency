using System.Text.Json;

namespace Usa.Game.Flow.Data;

/// <summary>
/// <c>content/nasa/manifest.json</c> (schema <c>nasa_manifest/1</c>, written by <c>tools/content</c>): every shipped
/// NASA item with its source record (GDD 16). The front end reads the main-menu backdrops, briefing images and
/// credits from it. A missing, empty or unreadable manifest is <see cref="Empty"/>: the screens fall back to no image.
/// </summary>
public sealed record NasaManifest(string Schema, string GeneratedAt, IReadOnlyList<NasaManifestItem> Items)
{
    public const string SchemaTag = "nasa_manifest/1";

    /// <summary>Relative to the project root (<c>res://</c>), not to <c>data/</c>.</summary>
    public const string RelativePath = "content/nasa/manifest.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        AllowTrailingCommas = true,
    };

    public static NasaManifest Empty { get; } = new(SchemaTag, "", []);

    /// <summary>Parses the manifest; on any problem returns <see cref="Empty"/> and says why in <paramref name="problem"/>.</summary>
    public static NasaManifest ParseOrEmpty(string? json, out string? problem)
    {
        problem = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            problem = "manifest missing or empty";
            return Empty;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<NasaManifest>(json, Options);
            if (manifest is null)
            {
                problem = "manifest is null";
                return Empty;
            }
            if (manifest.Schema != SchemaTag)
            {
                problem = $"expected schema '{SchemaTag}' but found '{manifest.Schema}'";
                return Empty;
            }
            return manifest;
        }
        catch (JsonException ex)
        {
            problem = $"manifest unreadable: {ex.Message}";
            return Empty;
        }
    }

    /// <summary>Shipped items for one touchpoint (see <see cref="NasaTouchpoints"/>), sorted by key.</summary>
    public IReadOnlyList<NasaManifestItem> ForTouchpoint(string touchpoint) =>
        Items.Where(i => i.Shipped && string.Equals(i.Touchpoint, touchpoint, StringComparison.Ordinal))
            .OrderBy(i => i.Key, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Every shipped item, sorted by key (the credits list).</summary>
    public IReadOnlyList<NasaManifestItem> Shipped() =>
        Items.Where(i => i.Shipped).OrderBy(i => i.Key, StringComparer.Ordinal).ToArray();
}

/// <param name="Key"><c>&lt;source&gt;:&lt;itemId&gt;</c>, e.g. <c>apod:2024-05-01</c> (an APOD item id is its date).</param>
/// <param name="LocalPath">Path of the cached media relative to the project root.</param>
/// <param name="Era"><c>historical</c> or <c>modern_reference</c> (see <see cref="NasaEras"/>).</param>
public sealed record NasaManifestItem(
    string Key,
    string Provider,
    string Dataset,
    string ItemId,
    string SourceUrl,
    string MediaUrl,
    string RetrievalDate,
    string MetadataRetrievalDate,
    string Credit,
    string Licence,
    string ChecksumSha256,
    long Bytes,
    string MediaType,
    string LocalPath,
    string Touchpoint,
    string Era,
    bool Shipped,
    string? DisplayCaption)
{
    /// <summary><c>image_library</c>, <c>apod</c> or <c>epic</c> (see <see cref="NasaSources"/>).</summary>
    public string Source
    {
        get
        {
            var colon = Key.IndexOf(':', StringComparison.Ordinal);
            return colon > 0 ? Key[..colon] : "";
        }
    }

    public bool IsModernReference => string.Equals(Era, NasaEras.ModernReference, StringComparison.Ordinal);
}
