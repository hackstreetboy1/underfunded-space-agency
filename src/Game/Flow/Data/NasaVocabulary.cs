namespace Usa.Game.Flow.Data;

/// <summary>Manifest <c>touchpoint</c> values (tools/content/nasa_curation/models.py).</summary>
public static class NasaTouchpoints
{
    public const string Briefing = "briefing";
    public const string Debrief = "debrief";
    public const string Encyclopedia = "encyclopedia";
    public const string MainMenu = "main_menu";
}

/// <summary>Manifest <c>era</c> values; APOD and EPIC are always modern reference (GDD 16 era integrity).</summary>
public static class NasaEras
{
    public const string Historical = "historical";
    public const string ModernReference = "modern_reference";
}

/// <summary>The source part of a manifest key.</summary>
public static class NasaSources
{
    public const string ImageLibrary = "image_library";
    public const string Apod = "apod";
    public const string Epic = "epic";
}
