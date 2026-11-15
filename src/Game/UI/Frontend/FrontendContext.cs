using Usa.Game.Bootstrap;
using Usa.Game.Content;
using Usa.Game.Flow;
using Usa.Game.Flow.Data;
using Usa.Game.Flow.Profile;
using Usa.Game.Services.Logging;
using Usa.Game.UI.Frontend.Model;
using Usa.Game.UI.Theme;
using Usa.Simulation.Commands;
using Usa.Simulation.Data;
using Usa.Simulation.Scoring;

namespace Usa.Game.UI.Frontend;

/// <summary>
/// What every front-end scene binds to, built once by <see cref="FrontendShell"/> from the app's <see cref="GameServices"/>:
/// the router, the worded text (data first, approved Figma copy as fallback), the NASA manifest, the encyclopedia, the
/// difficulties, the profile store, the grade bands and the active token set (Default or High contrast).
/// </summary>
public sealed class FrontendContext
{
    private const string Category = "Frontend";

    private FrontendContext(
        GameServices services,
        FrontendText text,
        NasaManifest manifest,
        Encyclopedia? encyclopedia,
        IReadOnlyList<DifficultyInfo> difficulties,
        IReadOnlyList<GradeBand> bands,
        ApodBackdrop? apod)
    {
        Services = services;
        Text = text;
        Manifest = manifest;
        Encyclopedia = encyclopedia;
        Difficulties = difficulties;
        Bands = bands;
        Apod = apod;
        Profiles = new SaveServiceProfileStore(services.Saves);
    }

    public GameServices Services { get; }

    public ScreenRouter Router => Services.Router;

    public Log Log => Services.Log;

    public FrontendText Text { get; }

    /// <summary>The loaded <c>content/strings.json</c>; the flow models take it directly.</summary>
    public StringTable Strings => Text.Strings ?? throw new InvalidOperationException("content/strings.json did not load.");

    public NasaManifest Manifest { get; }

    /// <summary>Null when <c>catalog/encyclopedia.json</c> failed to load (logged).</summary>
    public Encyclopedia? Encyclopedia { get; }

    public IReadOnlyList<DifficultyInfo> Difficulties { get; }

    public IProfileStore Profiles { get; }

    /// <summary>Grade bands of <c>config/scoring.json</c>; empty when it failed to load (the band line then shows no range).</summary>
    public IReadOnlyList<GradeBand> Bands { get; }

    /// <summary>The loading screens' Astronomy Picture of the Day; null when the services have none (tests).</summary>
    public ApodBackdrop? Apod { get; }

    /// <summary>Default or High contrast, following the setting (set by the shell before any scene builds).</summary>
    public UiTokenSet Tokens { get; internal set; } = UiTokens.Default;

    public bool ReducedMotion => Services.Settings.Current.Display.ReducedMotion;

    /// <exception cref="DefinitionException"><c>content/strings.json</c> is missing or invalid: nothing can be worded.</exception>
    public static FrontendContext Create(GameServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var log = services.Log;
        var strings = StringTable.Load(services.Data);
        var text = new FrontendText(strings);
        var manifest = Flow.Nodes.GodotFlowResources.LoadNasaManifest(log);

        Encyclopedia? encyclopedia = null;
        try
        {
            encyclopedia = Encyclopedia.Load(services.Data);
        }
        catch (DefinitionException ex)
        {
            log.Error(Category, $"encyclopedia not loaded: {ex.Message}");
        }

        IReadOnlyList<DifficultyInfo> difficulties = [];
        try
        {
            difficulties = DifficultyInfo.LoadAll(services.Data);
        }
        catch (DefinitionException ex)
        {
            log.Error(Category, $"difficulties not loaded: {ex.Message}");
        }

        IReadOnlyList<GradeBand> bands = [];
        try
        {
            bands = ScoringConfigLoader.Load(new DefinitionLoader(services.Data)).Grades;
        }
        catch (DefinitionException ex)
        {
            log.Warn(Category, $"grade bands not loaded, the debrief band line shows no range: {ex.Message}");
        }

        ApodBackdrop? apod = null;
        if (services.Apod is { } apodService)
        {
            AiGuardrails? guardrails = null;
            try
            {
                guardrails = AiGuardrails.Load(new DefinitionLoader(services.Data));
            }
            catch (DefinitionException ex)
            {
                log.Warn(Category, $"guardrails not loaded, picture titles stay hidden: {ex.Message}");
            }

            apod = new ApodBackdrop(apodService, guardrails, strings);
        }

        return new FrontendContext(services, text, manifest, encyclopedia, difficulties, bands, apod);
    }

    /// <summary>The mission's content library; null (logged) when it fails to load.</summary>
    public ContentLibrary? Content(string missionId)
    {
        try
        {
            return Services.Content.Get(missionId);
        }
        catch (DefinitionException ex)
        {
            Log.Error(Category, $"content for {missionId} not loaded: {ex.Message}");
            return null;
        }
    }

    /// <summary>The first shipped mission (the slice has one).</summary>
    public string? FirstMissionId => Services.Missions.MissionIds.Count > 0 ? Services.Missions.MissionIds[0] : null;

    public DifficultyInfo? Difficulty(string id) => Difficulties.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal));

    /// <summary>"Normal": the strings' label, else the difficulty file's name, else the id.</summary>
    public string DifficultyLabel(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (Strings.TryGet($"missionSelect.{id}", out var label)) return label;
        return Difficulty(id)?.DisplayName ?? id;
    }

    /// <summary>The slice's mission catalog (mission 1 and the locked campaign cards); null without mission content.</summary>
    public MissionCatalog? Catalog()
    {
        if (FirstMissionId is not { } id || Content(id) is not { } content) return null;
        return MissionCatalog.ForSlice(content.Briefing, Strings);
    }
}
