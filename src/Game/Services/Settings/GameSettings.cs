namespace Usa.Game.Services.Settings;

/// <summary>
/// Player settings persisted to <c>user://settings.json</c> (UI.md 5 Settings, GDD 17). Godot-free so migration and
/// sanitising can be unit-tested. Always pass through <see cref="SettingsRules.Sanitize"/> before use.
/// </summary>
public sealed record GameSettings
{
    /// <summary>
    /// 2 added <c>captions.enabled</c> (on for older files) and the optional <c>backendAutoStart</c>; 3 made borderless
    /// fullscreen the default display mode (an older file that says windowed moves to fullscreen once).
    /// </summary>
    public const int CurrentSchemaVersion = 3;
    public const string DefaultBackendUrl = "http://127.0.0.1:8787/";

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public AudioSettings Audio { get; init; } = new();

    public CaptionSettings Captions { get; init; } = new();

    public DisplaySettings Display { get; init; } = new();

    public ControlSettings Controls { get; init; } = new();

    public VoiceSettings Voice { get; init; } = new();

    public GameplaySettings Gameplay { get; init; } = new();

    /// <summary>Local proxy URL. Only loopback hosts are accepted, so voice and text never go anywhere else directly.</summary>
    public string BackendUrl { get; init; } = DefaultBackendUrl;

    /// <summary>
    /// "Start the local proxy automatically" (development convenience, read once at start-up by <c>GodotBackendLaunch.ResolveOptions</c>).
    /// Null until the player chooses: the build default then applies (on in debug builds, <c>USA_BACKEND_AUTOSTART</c> overrides).
    /// </summary>
    public bool? BackendAutoStart { get; init; }
}

/// <summary>Bus volumes as linear gain 0..1 (the slider value); 0 mutes the bus.</summary>
public sealed record AudioSettings
{
    public float MasterLinear { get; init; } = 1f;

    public float SpeechLinear { get; init; } = 1f;

    public float AlarmsLinear { get; init; } = 0.8f;

    public float SfxLinear { get; init; } = 0.8f;

    public float AmbienceLinear { get; init; } = 0.6f;

    public float UiLinear { get; init; } = 0.7f;
}

public sealed record CaptionSettings
{
    public const int MinSizePx = 18;
    public const int MaxSizePx = 32;

    /// <summary>Show a caption for every spoken line. On by default, so speech is never the only channel unless the player turns it off.</summary>
    public bool Enabled { get; init; } = true;

    public int SizePx { get; init; } = 22;

    /// <summary>Caption backing opacity 0..1.</summary>
    public float BackgroundOpacity { get; init; } = 0.75f;

    public bool ShowSpeakerNames { get; init; } = true;
}

/// <summary>
/// <see cref="Fullscreen"/> is a borderless window covering the monitor at its native resolution (1080p, 1440p, 4K,
/// ultrawide); the UI follows through <c>canvas_items</c> stretch from the 1920 x 1080 base.
/// </summary>
public enum WindowMode
{
    Windowed,
    Fullscreen,
    ExclusiveFullscreen,
}

public sealed record DisplaySettings
{
    public const float MinUiScale = 0.8f;
    public const float MaxUiScale = 1.5f;

    /// <summary>UI scale factor 0.8-1.5 (GDD 17: 80-150 %).</summary>
    public float UiScale { get; init; } = 1f;

    public bool HighContrast { get; init; }

    /// <summary>No camera shake, softer reveal transition.</summary>
    public bool ReducedMotion { get; init; }

    public WindowMode WindowMode { get; init; } = WindowMode.Fullscreen;
}

public enum BindingDevice
{
    Key,
    MouseButton,
}

/// <summary>
/// A serialisable input binding. <see cref="Code"/> is a Godot <c>Key</c> name for keys (physical keycode,
/// e.g. "Space") or a <c>MouseButton</c> name for mouse buttons (e.g. "Xbutton1").
/// </summary>
public sealed record BindingSpec(BindingDevice Device, string Code, bool Ctrl = false, bool Shift = false, bool Alt = false);

public sealed record ControlSettings
{
    public const int MaxBindingsPerAction = 4;

    /// <summary>
    /// Rebound actions only, keyed by <c>InputBindings</c> action name (e.g. "ptt"). Actions not listed keep the
    /// defaults from <c>InputBindings.RegisterDefaults</c>.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<BindingSpec>> BindingOverrides { get; init; } =
        new Dictionary<string, IReadOnlyList<BindingSpec>>(StringComparer.Ordinal);
}

public sealed record VoiceSettings
{
    public const string DefaultInputDevice = "Default";

    /// <summary>
    /// Off = text-only play (GDD 15.5); nothing is recorded or sent. Off by default in the demo build (docs/dev/DEMO_CUTS.md):
    /// typed orders only unless the player turns the mic on.
    /// </summary>
    public bool MicEnabled { get; init; }

    /// <summary>An entry of <c>AudioServer.GetInputDeviceList()</c>.</summary>
    public string InputDevice { get; init; } = DefaultInputDevice;
}

public sealed record GameplaySettings
{
    public bool TutorialCallouts { get; init; } = true;
}
