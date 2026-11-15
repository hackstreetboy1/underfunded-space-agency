using Godot;
using Usa.Game.Input;
using Usa.Game.Presentation.ViewModels;
using Usa.Game.Services.Audio;
using Usa.Game.Services.Logging;
using Usa.Game.Services.Storage;

namespace Usa.Game.Services.Settings;

/// <param name="Previous">Settings before the change.</param>
public sealed record SettingsChange(GameSettings Previous, GameSettings Current);

/// <summary>
/// Owns <see cref="GameSettings"/>: loads <c>user://settings.json</c> (migrating and repairing it; a corrupt
/// file is moved aside and defaults are used), applies the engine-side parts (bus volumes, input rebinding,
/// window mode, mic input device) and saves atomically on every change. UI scale, captions, high contrast,
/// reduced motion, tutorial callouts and the mic toggle are consumed by their owners through <see cref="Changed"/>
/// (captions on/off also through <see cref="CaptionsEnabled"/>); the proxy auto-start choice is read once at start-up.
/// A file this build must not replace (unreadable at start-up, or from a newer build) is never overwritten with
/// defaults; see <see cref="Persistence"/>. Never throws for IO problems. Main thread only.
/// </summary>
public sealed class SettingsService
{
    public const string FileName = "settings.json";
    private const string Category = "Settings";
    private const float SilentLinear = 0.0001f;

    private readonly UserFileStore _store;
    private readonly Log _log;

    // Every value the player changed while the file was unreadable; only those are replayed onto it once it reads.
    private readonly HashSet<string> _editedPaths = new(StringComparer.Ordinal);

    private readonly ObservableValue<bool> _captionsEnabled;

    private SettingsService(UserFileStore store, Log log, GameSettings initial, SettingsPersistence persistence)
    {
        _store = store;
        _log = log;
        Current = initial;
        Persistence = persistence;
        _captionsEnabled = new ObservableValue<bool>(initial.Captions.Enabled);
    }

    public event Action<SettingsChange>? Changed;

    public GameSettings Current { get; private set; }

    /// <summary>
    /// The Captions on/off setting, for the console's caption panel: subscribe and hide or show the panel (cues that arrive while
    /// it is off are not shown). Updates before <see cref="Changed"/> is raised.
    /// </summary>
    public IReadOnlyObservable<bool> CaptionsEnabled => _captionsEnabled;

    /// <summary>Whether changes reach the file; anything but <see cref="SettingsPersistence.Writable"/> means they last for this session only.</summary>
    public SettingsPersistence Persistence { get; private set; }

    /// <summary>Reads settings without touching the engine; call <see cref="ApplyToEngine"/> once input actions are registered.</summary>
    public static SettingsService Load(UserFileStore store, Log log)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(log);

        store.PruneLeftovers(string.Empty);
        var decoded = SettingsCodec.Decode(store.ReadText(FileName));
        var service = new SettingsService(store, log, decoded.Settings, Adopt(decoded, store, log));
        if (decoded.Status == SettingsDecodeStatus.Migrated && service.Save()) log.Info(Category, "settings migrated");
        return service;
    }

    /// <summary>
    /// Replaces the settings, applies them, saves, and raises <see cref="Changed"/>. Returns false if nothing was
    /// written (an IO failure, or <see cref="Persistence"/> forbids it); the settings still apply for this session.
    /// </summary>
    public bool Update(GameSettings next) => Apply(next, explicitReset: false);

    public bool Update(Func<GameSettings, GameSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return Update(change(Current));
    }

    /// <summary>
    /// Restores the defaults. As an explicit player choice it also ends <see cref="SettingsPersistence.ReadFailed"/>,
    /// so the defaults are saved; a newer build's file is still never overwritten.
    /// </summary>
    public bool ResetToDefaults() => Apply(new GameSettings(), explicitReset: true);

    /// <summary>
    /// Like <see cref="Update(GameSettings)"/>, but an explicit player choice to replace everything (the settings screen's
    /// "Reset to defaults" then Apply): it also ends <see cref="SettingsPersistence.ReadFailed"/>, so the file is overwritten.
    /// </summary>
    public bool Replace(GameSettings next) => Apply(next, explicitReset: true);

    /// <summary>
    /// Applies everything engine-side. Pass the previous settings to skip unchanged window mode. At start-up (no previous
    /// settings) a window chosen on the engine's command line (<c>--windowed</c>, <c>--resolution</c>, ...) is kept for the session.
    /// </summary>
    public void ApplyToEngine(GameSettings? previous = null)
    {
        ApplyAudio(Current.Audio);
        ApplyBindings(Current.Controls);
        if (previous is null && SettingsRules.CommandLineSetsWindow(OS.GetCmdlineArgs()))
            _log.Info(Category, "window chosen on the command line; display mode setting not applied this session");
        else if (previous is null || previous.Display.WindowMode != Current.Display.WindowMode)
            ApplyWindowMode(Current.Display.WindowMode);
        ApplyInputDevice(Current.Voice);
    }

    /// <summary>F11 / Alt+Enter: switches between windowed and borderless fullscreen and saves the choice.</summary>
    public bool ToggleFullscreen() =>
        Update(s => s with { Display = s.Display with { WindowMode = SettingsRules.ToggledWindowMode(s.Display.WindowMode) } });

    private static SettingsPersistence Adopt(SettingsDecodeResult decoded, UserFileStore store, Log log)
    {
        switch (decoded.Status)
        {
            case SettingsDecodeStatus.ReadFailed:
                log.Warn(Category, "settings unreadable (IO); using defaults and not saving until the file can be read");
                return SettingsPersistence.ReadFailed;
            case SettingsDecodeStatus.NewerVersion:
                log.Warn(Category, "settings written by a newer build; read what this build understands, the file is never overwritten");
                return SettingsPersistence.NewerFile;
            case SettingsDecodeStatus.Unreadable:
                store.MoveAside(FileName);
                log.Warn(Category, "settings file was corrupt; defaults restored");
                return SettingsPersistence.Writable;
            default:
                return SettingsPersistence.Writable;
        }
    }

    private bool Apply(GameSettings next, bool explicitReset)
    {
        ArgumentNullException.ThrowIfNull(next);
        var previous = Current;
        var target = SettingsRules.Sanitize(next);
        if (Persistence == SettingsPersistence.ReadFailed)
        {
            if (explicitReset) Persistence = SettingsPersistence.Writable;
            else target = RetryRead(previous, target);
        }

        Current = target;
        ApplyToEngine(previous);
        var saved = Save();
        _captionsEnabled.Set(Current.Captions.Enabled);
        Changed?.Invoke(new SettingsChange(previous, Current));
        return saved;
    }

    /// <summary>Re-reads a file that failed at start-up; once it reads, every value changed this session is replayed on top of what it holds.</summary>
    private GameSettings RetryRead(GameSettings previous, GameSettings edited)
    {
        SettingsCodec.CollectChangedPaths(previous, edited, _editedPaths);
        var decoded = SettingsCodec.Decode(_store.ReadText(FileName));
        if (decoded.Status == SettingsDecodeStatus.ReadFailed) return edited;

        Persistence = Adopt(decoded, _store, _log);
        _log.Info(Category, "settings file readable again; this session's changes were applied on top of it");
        return decoded.Status is SettingsDecodeStatus.Missing or SettingsDecodeStatus.Unreadable
            ? edited
            : SettingsCodec.ReplayPaths(decoded.Settings, edited, _editedPaths);
    }

    private bool Save()
    {
        if (Persistence != SettingsPersistence.Writable)
        {
            _log.Info(Category, $"settings applied for this session only ({Persistence})");
            return false;
        }

        var ok = _store.WriteTextAtomic(FileName, SettingsCodec.Encode(Current));
        if (!ok) _log.Warn(Category, "settings not saved");
        return ok;
    }

    private static void ApplyAudio(AudioSettings audio)
    {
        SetBusVolume(AudioBuses.Master, audio.MasterLinear);
        SetBusVolume(AudioBuses.Speech, audio.SpeechLinear);
        SetBusVolume(AudioBuses.Alarms, audio.AlarmsLinear);
        SetBusVolume(AudioBuses.Sfx, audio.SfxLinear);
        SetBusVolume(AudioBuses.Ambience, audio.AmbienceLinear);
        SetBusVolume(AudioBuses.Ui, audio.UiLinear);
    }

    private static void SetBusVolume(string bus, float linear)
    {
        var index = AudioBuses.Ensure(bus);
        var silent = linear <= SilentLinear;
        AudioServer.SetBusMute(index, silent);
        if (!silent) AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(linear));
    }

    private void ApplyBindings(ControlSettings controls)
    {
        // Start from the defaults so removing an override restores them.
        InputBindings.RegisterDefaults();
        foreach (var (action, specs) in controls.BindingOverrides)
        {
            if (!InputMap.HasAction(action))
            {
                _log.Warn(Category, $"binding override for unknown action '{action}' ignored");
                continue;
            }

            InputMap.ActionEraseEvents(action);
            foreach (var spec in specs)
            {
                if (InputBindingCodec.ToEvent(spec) is { } inputEvent) InputMap.ActionAddEvent(action, inputEvent);
                else _log.Warn(Category, $"unrecognised binding {spec.Device}:{spec.Code} for '{action}' ignored");
            }
        }
    }

    // Fullscreen is a borderless window at the monitor's native resolution; the root's canvas_items stretch and
    // ThemeApplier's UI scale (on the window's size change) do the rest.
    private static void ApplyWindowMode(WindowMode mode)
    {
        if (DisplayServer.GetName() == "headless") return;
        DisplayServer.WindowSetMode(mode switch
        {
            WindowMode.Fullscreen => DisplayServer.WindowMode.Fullscreen,
            WindowMode.ExclusiveFullscreen => DisplayServer.WindowMode.ExclusiveFullscreen,
            _ => DisplayServer.WindowMode.Windowed,
        });
        if (mode == WindowMode.Windowed) RestoreWindowedSize();
    }

    // A game that started fullscreen has no earlier windowed size, so the restored window would still cover the screen.
    private static void RestoreWindowedSize()
    {
        var screen = DisplayServer.WindowGetCurrentScreen();
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        var window = DisplayServer.WindowGetSize();
        if (window.X < usable.Size.X && window.Y < usable.Size.Y) return;

        var (width, height) = SettingsRules.WindowedSize(usable.Size.X, usable.Size.Y);
        var size = new Vector2I(width, height);
        DisplayServer.WindowSetSize(size);
        DisplayServer.WindowSetPosition(usable.Position + (usable.Size - size) / 2);
    }

    private void ApplyInputDevice(VoiceSettings voice)
    {
        var devices = AudioServer.GetInputDeviceList();
        var device = Array.IndexOf(devices, voice.InputDevice) >= 0 ? voice.InputDevice : VoiceSettings.DefaultInputDevice;
        if (device != voice.InputDevice) _log.Info(Category, "saved input device not present; using the default device");
        if (AudioServer.InputDevice != device) AudioServer.InputDevice = device;
    }
}
