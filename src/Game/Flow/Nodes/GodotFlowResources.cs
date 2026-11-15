using Godot;
using Usa.Game.Flow.Data;
using Usa.Game.Input;
using Usa.Game.Services.Logging;
using Usa.Game.Services.Settings;

namespace Usa.Game.Flow.Nodes;

/// <summary>Engine-side inputs the pure flow models need: the NASA manifest and the default input bindings.</summary>
public static class GodotFlowResources
{
    private const string Category = "Flow";

    private static readonly StringName[] RebindableActions =
    [
        InputBindings.PushToTalk,
        InputBindings.FocusComms,
        InputBindings.View1,
        InputBindings.View2,
        InputBindings.View3,
        InputBindings.View4,
        InputBindings.AcknowledgeAlarms,
        InputBindings.Search,
        InputBindings.FocusVehicle,
        InputBindings.AdvanceEvent,
        InputBindings.Encyclopedia,
        InputBindings.Menu,
    ];

    /// <summary>Reads <c>res://content/nasa/manifest.json</c>; any problem is logged and yields an empty manifest.</summary>
    public static NasaManifest LoadNasaManifest(Log log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var path = "res://" + NasaManifest.RelativePath;
        string? json = null;
        if (Godot.FileAccess.FileExists(path))
        {
            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            json = file?.GetAsText();
        }

        var manifest = NasaManifest.ParseOrEmpty(json, out var problem);
        if (problem is not null) log.Warn(Category, $"NASA manifest not used: {problem}");
        return manifest;
    }

    /// <summary>
    /// The default bindings of every rebindable action. Re-registers the defaults to read them, then re-applies the
    /// player's overrides, so call it once at start-up (after <c>SettingsService.ApplyToEngine</c>).
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<BindingSpec>> CaptureDefaultBindings(SettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        InputBindings.RegisterDefaults();
        var defaults = new Dictionary<string, IReadOnlyList<BindingSpec>>(StringComparer.Ordinal);
        foreach (var action in RebindableActions) defaults[action.ToString()] = InputBindingCodec.Current(action);
        // Unchanged settings as "previous": the window mode was applied at boot and is not re-applied here.
        settings.ApplyToEngine(settings.Current);
        return defaults;
    }
}
