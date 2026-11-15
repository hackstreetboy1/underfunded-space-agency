using System.Text.Json.Nodes;
using Usa.Game.Services.Storage;

namespace Usa.Game.Services.Settings;

/// <summary>Validation, clamping and schema migration for <see cref="GameSettings"/>. Pure.</summary>
public static class SettingsRules
{
    private static readonly HashSet<string> LoopbackHosts = new(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "localhost", "[::1]", "::1" };

    /// <summary>
    /// Migration hook: add a step <c>n → n+1</c> here whenever <see cref="GameSettings.CurrentSchemaVersion"/> grows.
    /// Version 0 (a file without <c>schemaVersion</c>) has the version-1 shape.
    /// </summary>
    public static SchemaMigrator CreateMigrator() => new(GameSettings.CurrentSchemaVersion, new Dictionary<int, Func<JsonObject, JsonObject>>
    {
        [0] = root => root,
        [1] = AddCaptionsSwitch,
        [2] = MoveToFullscreen,
    });

    /// <summary>The smallest windowed size the window is restored to when it leaves fullscreen (the old 1600 x 900 default).</summary>
    public const int PreferredWindowedWidthPx = 1600;

    public const int PreferredWindowedHeightPx = 900;

    /// <summary>Share of the screen a restored window may cover, so its title bar and the taskbar stay reachable.</summary>
    public const float MaxWindowedScreenShare = 0.85f;

    // Godot's window-mode and size flags: any of them on the command line (a capture at a fixed size) wins for that session.
    // --position and --screen are not among them: the editor adds them to every run.
    private static readonly HashSet<string> WindowCommandLineFlags = new(StringComparer.Ordinal)
    {
        "--windowed", "-w", "--fullscreen", "-f", "--maximized", "-m", "--resolution",
    };

    /// <summary>1 → 2: captions were always shown, so an older file keeps them on. A malformed section is left to the decoder.</summary>
    private static JsonObject AddCaptionsSwitch(JsonObject root)
    {
        if (root["captions"] is JsonObject captions && !captions.ContainsKey("enabled")) captions["enabled"] = true;
        return root;
    }

    /// <summary>
    /// 2 → 3: windowed was the default and every file stored it, so a stored "Windowed" cannot be told from a choice; the
    /// game now opens fullscreen, and the player can pick windowed again (Settings, F11, Alt+Enter). Other values stay.
    /// </summary>
    private static JsonObject MoveToFullscreen(JsonObject root)
    {
        if (root["display"] is JsonObject display && display["windowMode"] is JsonValue mode
            && mode.TryGetValue<string>(out var name) && name == nameof(WindowMode.Windowed))
            display["windowMode"] = nameof(WindowMode.Fullscreen);
        return root;
    }

    /// <summary>F11 / Alt+Enter: windowed goes to borderless fullscreen, either fullscreen mode back to windowed.</summary>
    public static WindowMode ToggledWindowMode(WindowMode mode) => mode == WindowMode.Windowed ? WindowMode.Fullscreen : WindowMode.Windowed;

    /// <summary>True when the engine's command line already chose the window (<c>--windowed</c>, <c>--resolution</c>, ...), so start-up leaves it alone.</summary>
    public static bool CommandLineSetsWindow(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        foreach (var arg in args)
        {
            var name = arg.Split('=', 2)[0];
            if (WindowCommandLineFlags.Contains(name)) return true;
        }
        return false;
    }

    /// <summary>
    /// The size a window restored from fullscreen gets: <see cref="PreferredWindowedWidthPx"/> x <see cref="PreferredWindowedHeightPx"/>,
    /// shrunk (keeping 16:9) to at most <see cref="MaxWindowedScreenShare"/> of a smaller screen.
    /// </summary>
    public static (int WidthPx, int HeightPx) WindowedSize(int screenWidthPx, int screenHeightPx)
    {
        if (screenWidthPx <= 0 || screenHeightPx <= 0) return (PreferredWindowedWidthPx, PreferredWindowedHeightPx);
        var fit = Math.Min(1f, MaxWindowedScreenShare * Math.Min(
            screenWidthPx / (float)PreferredWindowedWidthPx, screenHeightPx / (float)PreferredWindowedHeightPx));
        return ((int)MathF.Round(PreferredWindowedWidthPx * fit), (int)MathF.Round(PreferredWindowedHeightPx * fit));
    }

    /// <summary>Repairs every out-of-range or missing value instead of rejecting the file.</summary>
    public static GameSettings Sanitize(GameSettings? settings)
    {
        var defaults = new GameSettings();
        if (settings is null) return defaults;

        var audio = settings.Audio ?? defaults.Audio;
        var captions = settings.Captions ?? defaults.Captions;
        var display = settings.Display ?? defaults.Display;
        var voice = settings.Voice ?? defaults.Voice;

        return settings with
        {
            SchemaVersion = GameSettings.CurrentSchemaVersion,
            Audio = new AudioSettings
            {
                MasterLinear = Unit(audio.MasterLinear, defaults.Audio.MasterLinear),
                SpeechLinear = Unit(audio.SpeechLinear, defaults.Audio.SpeechLinear),
                AlarmsLinear = Unit(audio.AlarmsLinear, defaults.Audio.AlarmsLinear),
                SfxLinear = Unit(audio.SfxLinear, defaults.Audio.SfxLinear),
                AmbienceLinear = Unit(audio.AmbienceLinear, defaults.Audio.AmbienceLinear),
                UiLinear = Unit(audio.UiLinear, defaults.Audio.UiLinear),
            },
            Captions = captions with
            {
                SizePx = Math.Clamp(captions.SizePx, CaptionSettings.MinSizePx, CaptionSettings.MaxSizePx),
                BackgroundOpacity = Unit(captions.BackgroundOpacity, defaults.Captions.BackgroundOpacity),
            },
            Display = display with
            {
                UiScale = float.IsFinite(display.UiScale)
                    ? Math.Clamp(display.UiScale, DisplaySettings.MinUiScale, DisplaySettings.MaxUiScale)
                    : defaults.Display.UiScale,
                WindowMode = Enum.IsDefined(display.WindowMode) ? display.WindowMode : defaults.Display.WindowMode,
            },
            Controls = new ControlSettings { BindingOverrides = SanitizeBindings(settings.Controls?.BindingOverrides) },
            Voice = voice with
            {
                InputDevice = string.IsNullOrWhiteSpace(voice.InputDevice) ? VoiceSettings.DefaultInputDevice : voice.InputDevice,
            },
            Gameplay = settings.Gameplay ?? defaults.Gameplay,
            BackendUrl = IsAllowedBackendUrl(settings.BackendUrl) ? NormalizeUrl(settings.BackendUrl) : GameSettings.DefaultBackendUrl,
        };
    }

    public static bool IsAllowedBackendUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && LoopbackHosts.Contains(uri.Host)
        && string.IsNullOrEmpty(uri.UserInfo)
        // Checked on the raw text so a query or fragment is refused even when empty ("/?", "/#"); only a bare base URL is allowed.
        && url.AsSpan().IndexOfAny('?', '#') < 0;

    private static string NormalizeUrl(string url)
    {
        var absolute = new Uri(url, UriKind.Absolute).AbsoluteUri;
        return absolute.EndsWith('/') ? absolute : absolute + "/";
    }

    private static Dictionary<string, IReadOnlyList<BindingSpec>> SanitizeBindings(IReadOnlyDictionary<string, IReadOnlyList<BindingSpec>>? overrides)
    {
        var result = new Dictionary<string, IReadOnlyList<BindingSpec>>(StringComparer.Ordinal);
        if (overrides is null) return result;

        foreach (var (action, specs) in overrides)
        {
            if (string.IsNullOrWhiteSpace(action) || specs is null) continue;
            var valid = new List<BindingSpec>();
            foreach (var spec in specs)
            {
                if (spec is null || string.IsNullOrWhiteSpace(spec.Code) || !Enum.IsDefined(spec.Device) || valid.Contains(spec)) continue;
                valid.Add(spec);
                if (valid.Count == ControlSettings.MaxBindingsPerAction) break;
            }
            result[action] = valid;
        }
        return result;
    }

    private static float Unit(float value, float fallback) => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : fallback;
}
