using System.Globalization;
using Usa.Game.Content;
using Usa.Game.Services.Settings;

namespace Usa.Game.Flow.Screens;

/// <summary>Settings tabs (UI.md 5).</summary>
public enum SettingsTab
{
    Audio,
    Captions,
    Display,
    Controls,
    VoiceAndPrivacy,
    Gameplay,
}

public enum BindingCaptureOutcome
{
    Bound,

    /// <summary>Esc pressed during capture: nothing changed.</summary>
    Cancelled,
    NotCapturing,

    /// <summary>Esc and the left mouse button stay with the menu and the UI.</summary>
    Reserved,

    /// <summary>Another action already uses the input; see <see cref="BindingCaptureResult.ConflictingAction"/>.</summary>
    Conflict,

    /// <summary>The action already has this input in another slot.</summary>
    Duplicate,
}

public sealed record BindingCaptureResult(BindingCaptureOutcome Outcome, string? ConflictingAction = null);

/// <summary>A rebind prompt waiting for the next key or mouse button.</summary>
/// <param name="Slot">Which binding of the action is replaced; a slot past the end adds one.</param>
public sealed record BindingCapture(string Action, int Slot);

/// <param name="Field">Dotted settings path, e.g. <c>display.uiScale</c> or <c>controls.ptt</c>.</param>
public sealed record SettingsProblem(string Field, string Message);

/// <summary>
/// Settings screen (UI.md 5, GDD 17): edits a draft copy of <see cref="GameSettings"/>; Apply validates and hands it to
/// the settings service, Cancel restores the last applied values. Owns the rebinding prompt (PTT key plus mouse
/// alternative and the rest of the rebind list) and exposes the voice privacy notice.
/// </summary>
public sealed class SettingsModel
{
    public const string PushToTalkAction = "ptt";
    public const string MenuAction = "menu";

    private static readonly BindingSpec EscapeKey = new(BindingDevice.Key, "Escape");
    private static readonly BindingSpec LeftMouse = new(BindingDevice.MouseButton, "Left");

    private readonly ISettingsSink _sink;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<BindingSpec>> _defaults;
    private readonly StringTable _strings;
    private bool _resetPending;

    /// <param name="defaultBindings">Every rebindable action with its default bindings (<c>InputBindings.RegisterDefaults</c>).</param>
    public SettingsModel(ISettingsSink sink, StringTable strings, IReadOnlyDictionary<string, IReadOnlyList<BindingSpec>> defaultBindings)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(defaultBindings);
        _sink = sink;
        _defaults = defaultBindings;
        _strings = strings;
        Baseline = sink.Current;
        Draft = Baseline;
        Actions = defaultBindings.Keys
            .OrderBy(a => a == PushToTalkAction ? 0 : 1)
            .ThenBy(a => a, StringComparer.Ordinal)
            .ToArray();

        PrivacyTitle = strings.Get("privacy.title");
        PrivacyNotice = strings.Get("privacy.notice");
        MicOnLabel = strings.Get("privacy.micOn");
        MicOffLabel = strings.Get("privacy.micOff");
        InputDeviceLabel = strings.Get("privacy.inputDevice");
        TipFrequencyNote = strings.Get("settings.tipFrequencyNote");
    }

    public event Action? Changed;

    /// <summary>The settings as last applied.</summary>
    public GameSettings Baseline { get; private set; }

    /// <summary>The edited copy; nothing reaches the game until <see cref="Apply"/>.</summary>
    public GameSettings Draft { get; private set; }

    public bool IsDirty => !AreEqual(Draft, Baseline);

    public SettingsTab ActiveTab { get; private set; } = SettingsTab.Audio;

    /// <summary>Rebindable actions, push-to-talk first.</summary>
    public IReadOnlyList<string> Actions { get; }

    public BindingCapture? Capture { get; private set; }

    public string PrivacyTitle { get; }

    /// <summary>Voice audio and text are sent to ElevenLabs and Google through the local proxy (GDD 15.5).</summary>
    public string PrivacyNotice { get; }

    public string MicOnLabel { get; }

    public string MicOffLabel { get; }

    public string InputDeviceLabel { get; }

    /// <summary>Gameplay tab: tip frequency is bound to the difficulty, so it is shown read-only.</summary>
    public string TipFrequencyNote { get; }

    public IReadOnlyList<SettingsProblem> Problems => Validate(Draft, _defaults, _strings);

    public bool CanApply => IsDirty && Problems.Count == 0;

    public void SelectTab(SettingsTab tab)
    {
        if (ActiveTab == tab) return;
        ActiveTab = tab;
        Changed?.Invoke();
    }

    public void Edit(Func<GameSettings, GameSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Draft = change(Draft) ?? throw new InvalidOperationException("A settings edit returned null.");
        Changed?.Invoke();
    }

    /// <summary>Validates and applies the draft. False when it is invalid (nothing applied) or saving failed (applied for this session).</summary>
    public bool Apply()
    {
        if (Problems.Count > 0) return false;
        var saved = _resetPending ? _sink.Replace(Draft) : _sink.Update(Draft);
        _resetPending = false;
        Baseline = _sink.Current;
        Draft = Baseline;
        Capture = null;
        Changed?.Invoke();
        return saved;
    }

    /// <summary>Discards every edit since the last apply.</summary>
    public void Cancel()
    {
        Draft = Baseline;
        _resetPending = false;
        Capture = null;
        Changed?.Invoke();
    }

    /// <summary>Puts the defaults in the draft; Apply still decides, and then replaces the stored settings as a whole.</summary>
    public void ResetToDefaults()
    {
        Draft = new GameSettings { BackendUrl = Draft.BackendUrl };
        _resetPending = true;
        Capture = null;
        Changed?.Invoke();
    }

    /// <summary>The bindings the action would have after Apply: its override, else its defaults.</summary>
    public IReadOnlyList<BindingSpec> EffectiveBindings(string action) => Effective(Draft, _defaults, action);

    public bool BeginCapture(string action, int slot)
    {
        if (!_defaults.ContainsKey(action) || slot < 0 || slot >= ControlSettings.MaxBindingsPerAction) return false;
        Capture = new BindingCapture(action, slot);
        Changed?.Invoke();
        return true;
    }

    public void CancelCapture()
    {
        if (Capture is null) return;
        Capture = null;
        Changed?.Invoke();
    }

    /// <summary>Feeds the input pressed while the rebind prompt is open.</summary>
    public BindingCaptureResult CaptureInput(BindingSpec input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (Capture is not { } capture) return new BindingCaptureResult(BindingCaptureOutcome.NotCapturing);
        if (input == EscapeKey)
        {
            CancelCapture();
            return new BindingCaptureResult(BindingCaptureOutcome.Cancelled);
        }
        if (input == LeftMouse) return new BindingCaptureResult(BindingCaptureOutcome.Reserved);

        foreach (var other in Actions)
        {
            if (other != capture.Action && EffectiveBindings(other).Contains(input))
                return new BindingCaptureResult(BindingCaptureOutcome.Conflict, other);
        }

        var bindings = EffectiveBindings(capture.Action).ToList();
        var existing = bindings.IndexOf(input);
        if (existing >= 0 && existing != capture.Slot) return new BindingCaptureResult(BindingCaptureOutcome.Duplicate);

        if (capture.Slot < bindings.Count) bindings[capture.Slot] = input;
        else bindings.Add(input);
        Draft = WithBindings(Draft, capture.Action, bindings);
        Capture = null;
        Changed?.Invoke();
        return new BindingCaptureResult(BindingCaptureOutcome.Bound);
    }

    /// <summary>Back to the action's defaults (in the draft).</summary>
    public void ResetBindings(string action)
    {
        if (!_defaults.ContainsKey(action)) return;
        Draft = WithBindings(Draft, action, _defaults[action]);
        Changed?.Invoke();
    }

    /// <param name="text">Problem messages (<c>settings.problem*</c> strings).</param>
    public static IReadOnlyList<SettingsProblem> Validate(
        GameSettings settings, IReadOnlyDictionary<string, IReadOnlyList<BindingSpec>> defaults, ITextSource text)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(text);
        var problems = new List<SettingsProblem>();

        var audio = settings.Audio;
        Unit(problems, text, "audio.master", audio.MasterLinear);
        Unit(problems, text, "audio.speech", audio.SpeechLinear);
        Unit(problems, text, "audio.alarms", audio.AlarmsLinear);
        Unit(problems, text, "audio.sfx", audio.SfxLinear);
        Unit(problems, text, "audio.ambience", audio.AmbienceLinear);
        Unit(problems, text, "audio.ui", audio.UiLinear);

        if (settings.Captions.SizePx is < CaptionSettings.MinSizePx or > CaptionSettings.MaxSizePx)
        {
            problems.Add(new SettingsProblem("captions.size", text.Format(
                "settings.problemCaptionSize",
                ("min", CaptionSettings.MinSizePx.ToString(CultureInfo.InvariantCulture)),
                ("max", CaptionSettings.MaxSizePx.ToString(CultureInfo.InvariantCulture)))));
        }
        Unit(problems, text, "captions.backgroundOpacity", settings.Captions.BackgroundOpacity);

        var scale = settings.Display.UiScale;
        if (!float.IsFinite(scale) || scale < DisplaySettings.MinUiScale || scale > DisplaySettings.MaxUiScale)
            problems.Add(new SettingsProblem("display.uiScale", text.Get("settings.problemUiScale")));
        if (!Enum.IsDefined(settings.Display.WindowMode))
            problems.Add(new SettingsProblem("display.windowMode", text.Get("settings.problemWindowMode")));

        if (string.IsNullOrWhiteSpace(settings.Voice.InputDevice))
            problems.Add(new SettingsProblem("voice.inputDevice", text.Get("settings.problemInputDevice")));
        if (!SettingsRules.IsAllowedBackendUrl(settings.BackendUrl))
            problems.Add(new SettingsProblem("backendUrl", text.Get("settings.problemBackendUrl")));

        ValidateBindings(settings, defaults, text, problems);
        return problems;
    }

    private static void ValidateBindings(
        GameSettings settings, IReadOnlyDictionary<string, IReadOnlyList<BindingSpec>> defaults, ITextSource text, List<SettingsProblem> problems)
    {
        foreach (var action in settings.Controls.BindingOverrides.Keys.Order(StringComparer.Ordinal))
        {
            if (!defaults.ContainsKey(action))
                problems.Add(new SettingsProblem($"controls.{action}", text.Format("settings.problemUnknownAction", ("action", action))));
        }

        var owners = new Dictionary<BindingSpec, string>();
        foreach (var action in defaults.Keys.Order(StringComparer.Ordinal))
        {
            var bindings = Effective(settings, defaults, action);
            if (bindings.Count > ControlSettings.MaxBindingsPerAction)
                problems.Add(new SettingsProblem($"controls.{action}", text.Format(
                    "settings.problemTooManyBindings", ("count", ControlSettings.MaxBindingsPerAction.ToString(CultureInfo.InvariantCulture)))));
            if (action == PushToTalkAction && bindings.Count == 0)
                problems.Add(new SettingsProblem($"controls.{action}", text.Get("settings.problemPushToTalkUnbound")));

            foreach (var binding in bindings)
            {
                if (string.IsNullOrWhiteSpace(binding.Code))
                    problems.Add(new SettingsProblem($"controls.{action}", text.Get("settings.problemEmptyBinding")));
                else if (binding == LeftMouse || (binding == EscapeKey && action != MenuAction))
                    problems.Add(new SettingsProblem($"controls.{action}", text.Get("settings.problemReservedBinding")));
                else if (!owners.TryAdd(binding, action))
                    problems.Add(new SettingsProblem($"controls.{action}", text.Format("settings.problemDuplicateBinding", ("action", owners[binding]))));
            }
        }
    }

    private static IReadOnlyList<BindingSpec> Effective(GameSettings settings, IReadOnlyDictionary<string, IReadOnlyList<BindingSpec>> defaults, string action)
    {
        if (settings.Controls.BindingOverrides.TryGetValue(action, out var custom)) return custom;
        return defaults.TryGetValue(action, out var standard) ? standard : [];
    }

    private GameSettings WithBindings(GameSettings settings, string action, IReadOnlyList<BindingSpec> bindings)
    {
        var overrides = new Dictionary<string, IReadOnlyList<BindingSpec>>(settings.Controls.BindingOverrides, StringComparer.Ordinal);

        // An override equal to the defaults is dropped so the file only holds real changes.
        if (_defaults.TryGetValue(action, out var standard) && standard.SequenceEqual(bindings)) overrides.Remove(action);
        else overrides[action] = [.. bindings];
        return settings with { Controls = settings.Controls with { BindingOverrides = overrides } };
    }

    private static void Unit(List<SettingsProblem> problems, ITextSource text, string field, float value)
    {
        if (!float.IsFinite(value) || value < 0f || value > 1f) problems.Add(new SettingsProblem(field, text.Get("settings.problemPercent")));
    }

    private static bool AreEqual(GameSettings a, GameSettings b)
    {
        if (a with { Controls = b.Controls } != b) return false;
        var left = a.Controls.BindingOverrides;
        var right = b.Controls.BindingOverrides;
        if (left.Count != right.Count) return false;
        foreach (var (action, bindings) in left)
        {
            if (!right.TryGetValue(action, out var other) || !bindings.SequenceEqual(other)) return false;
        }
        return true;
    }
}
