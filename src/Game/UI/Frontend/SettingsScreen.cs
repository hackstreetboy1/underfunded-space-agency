using Godot;
using Usa.Game.Flow;
using Usa.Game.Flow.Nodes;
using Usa.Game.Flow.Screens;
using Usa.Game.Services.Backend;
using Usa.Game.Services.Settings;
using Usa.Game.UI.Frontend.Model;
using Usa.Game.UI.Theme;
using GameWindowMode = Usa.Game.Services.Settings.WindowMode;

namespace Usa.Game.UI.Frontend;

/// <summary>
/// Settings (<c>Settings.tscn</c>, Figma G6-05a/b; UI.md 5, GDD 17): the tab list (Audio, Captions, Display, Controls, Voice &amp;
/// privacy, Gameplay) and one pane per tab. "Changes apply immediately": every edit goes into <see cref="SettingsModel"/>'s
/// draft and is applied after a short debounce (sliders) or at once (toggles, lists, rebinds); invalid drafts are not applied
/// and their problems show under the pane. Includes the Captions on/off switch, the rebind list with key capture, the microphone
/// test (about 3 s of metering, nothing sent), the privacy notice, the local proxy address and its auto-start switch. Opened from
/// the Esc menu, Back returns to it.
/// </summary>
public sealed partial class SettingsScreen : Control, IFrontendScreen
{
    private const double ApplyDelayS = 0.3;

    private static readonly (SettingsTab Tab, string Key)[] TabKeys =
    [
        (SettingsTab.Audio, "settings.tabAudio"),
        (SettingsTab.Captions, "settings.tabCaptions"),
        (SettingsTab.Display, "settings.tabDisplay"),
        (SettingsTab.Controls, "settings.tabControls"),
        (SettingsTab.VoiceAndPrivacy, "settings.tabVoicePrivacy"),
        (SettingsTab.Gameplay, "settings.tabGameplay"),
    ];

    private Button _back = null!;
    private Label _title = null!;
    private Label _applyNote = null!;
    private Button _restore = null!;
    private VBoxContainer _tabs = null!;
    private VBoxContainer _pane = null!;
    private Label _problems = null!;
    private Godot.Timer _applyTimer = null!;
    private FrontendContext? _context;
    private SettingsModel? _model;
    private ProgressBar? _micLevel;
    private Button? _micTestButton;
    private Label? _micTestStatus;
    private readonly MicTest _micTest = new();
    private string? _captureMessage;
    private bool _builtFromCapture;

    public override void _Ready()
    {
        _back = GetNode<Button>("%Back");
        _title = GetNode<Label>("%Title");
        _applyNote = GetNode<Label>("%ApplyNote");
        _restore = GetNode<Button>("%Restore");
        _tabs = GetNode<VBoxContainer>("%Tabs");
        _pane = GetNode<VBoxContainer>("%Pane");
        _problems = GetNode<Label>("%Problems");
        _applyTimer = new Godot.Timer { OneShot = true, WaitTime = ApplyDelayS, ProcessCallback = Godot.Timer.TimerProcessCallback.Idle };
        _applyTimer.Timeout += ApplyNow;
        AddChild(_applyTimer);
        _back.Pressed += () =>
        {
            ApplyNow();
            _context?.Router.Back();
        };
        _restore.Pressed += () =>
        {
            _model?.ResetToDefaults();
            ApplyNow();
            BuildPane();
        };
        SetProcess(false);
    }

    public override void _ExitTree()
    {
        // Esc leaves through the router without the Back button: a pending slider edit still lands.
        ApplyNow();
        StopMicTest();
    }

    // Presentation only: the microphone level meter on the Voice & privacy pane, and the test take's clock.
    public override void _Process(double delta)
    {
        if (_micLevel is null || _context is null) return;
        var mic = _context.Services.Mic;
        _micLevel.Value = Math.Clamp(mic.InputLevel, 0f, 1f);
        if (_micTest.State != MicTestState.Listening) return;
        if (!mic.IsCapturing)
        {
            // Cancelled from elsewhere (a freeze, the mic switched off): no verdict.
            _micTest.Abort();
            ShowMicTest();
            return;
        }
        if (_micTest.Advance(delta, mic.InputLevel))
        {
            mic.CancelCapture();
            ShowMicTest();
        }
    }

    /// <summary>Records about 3 s for the level meter only: the take is cancelled, never released, so nothing is transcribed.</summary>
    private void StartMicTest()
    {
        if (_context is not { } context || _micTest.State == MicTestState.Listening) return;
        var mic = context.Services.Mic;
        _micTest.Begin(context.Services.Settings.Current.Voice.MicEnabled && mic.BeginCapture());
        ShowMicTest();
    }

    private void StopMicTest()
    {
        if (_micTest.State != MicTestState.Listening) return;
        _micTest.Abort();
        if (_context?.Services.Mic is { IsCapturing: true } mic) mic.CancelCapture();
    }

    private void ShowMicTest()
    {
        if (_context is not { } context || _micTestStatus is null || !IsInstanceValid(_micTestStatus)) return;
        var text = context.Text;
        _micTestStatus.Text = _micTest.State switch
        {
            MicTestState.Listening => text.Get("settings.micTestListening"),
            MicTestState.Heard => text.Format("settings.micTestHeard", ("percent", _micTest.PeakPercent.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            MicTestState.Silent => text.Get("settings.micTestSilent"),
            MicTestState.Unavailable => text.Get("settings.micTestUnavailable"),
            _ => string.Empty,
        };
        _micTestStatus.Visible = _micTest.State != MicTestState.Idle;
        if (_micTestButton is not null && IsInstanceValid(_micTestButton)) _micTestButton.Disabled = _micTest.State == MicTestState.Listening;
    }

    public void Initialize(FrontendContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        var text = context.Text;
        _model = new SettingsModel(new SettingsServiceSink(context.Services.Settings), context.Strings, context.Services.DefaultBindings);

        var fromMission = context.Router.SettingsReturn == Screen.Mission;
        _back.Text = fromMission ? text.Get("escMenu.title") : text.Get("mainMenu.back");
        Ui.SetIcon(_back, Ui.IconBack, context.Tokens.ColorTextMid);
        _title.Text = text.Get("settings.title");
        _applyNote.Text = text.Get("settings.applyNote");
        _restore.Text = text.Get("settings.restoreDefaults");

        Ui.Clear(_tabs);
        foreach (var (tab, key) in TabKeys)
        {
            var target = tab;
            var button = Ui.Button(text.Get(key), Ui.ButtonGhost, () => SelectTab(target));
            button.Alignment = HorizontalAlignment.Left;
            button.CustomMinimumSize = new Vector2(280, 44);
            button.ToggleMode = true;
            button.SetMeta("tab", (int)tab);
            _tabs.AddChild(button);
        }
        BuildPane();
        if (_tabs.GetChildCount() > 0) _tabs.GetChild<Button>(0).CallDeferred(Control.MethodName.GrabFocus);
    }

    public override void _Input(InputEvent @event)
    {
        if (_model?.Capture is null || @event.IsEcho() || !@event.IsPressed()) return;
        if (@event is InputEventKey { PhysicalKeycode: Key.Ctrl or Key.Shift or Key.Alt or Key.Meta }) return;
        if (@event is not (InputEventKey or InputEventMouseButton)) return;
        if (InputBindingCodec.FromEvent(@event) is not { } spec) return;

        GetViewport().SetInputAsHandled();
        var result = _model.CaptureInput(spec);
        _captureMessage = result.Outcome switch
        {
            BindingCaptureOutcome.Conflict => _context?.Text.Format("settings.problemDuplicateBinding", ("action", ActionLabel(result.ConflictingAction ?? ""))),
            BindingCaptureOutcome.Reserved => _context?.Text.Get("settings.problemReservedBinding"),
            BindingCaptureOutcome.Duplicate => _context?.Text.Format("settings.problemDuplicateBinding", ("action", ActionLabel(_model.Capture?.Action ?? ""))),
            _ => null,
        };
        if (result.Outcome == BindingCaptureOutcome.Bound) ApplyNow();
        BuildPane();
    }

    private void SelectTab(SettingsTab tab)
    {
        if (_model is null) return;
        _model.CancelCapture();
        _captureMessage = null;
        _model.SelectTab(tab);
        BuildPane();
    }

    private void Edit(Func<GameSettings, GameSettings> change, bool now)
    {
        if (_model is null) return;
        _model.Edit(change);
        if (now) ApplyNow();
        else _applyTimer.Start();
        ShowProblems();
    }

    private void ApplyNow()
    {
        _applyTimer?.Stop();
        if (_model is { CanApply: true } model) model.Apply();
        ShowProblems();
    }

    private void ShowProblems()
    {
        if (_model is null || _problems is null) return;
        var lines = _model.Problems.Select(p => p.Message).ToList();
        if (_captureMessage is not null) lines.Insert(0, _captureMessage);
        _problems.Text = string.Join("\n", lines);
        _problems.Visible = lines.Count > 0;
        if (_context is not null) _problems.AddThemeColorOverride("font_color", _context.Tokens.ColorCritical);
    }

    private void BuildPane()
    {
        if (_model is not { } model || _context is not { } context) return;
        var text = context.Text;
        var tokens = context.Tokens;
        foreach (var child in _tabs.GetChildren())
        {
            if (child is not Button button) continue;
            var selected = button.GetMeta("tab").AsInt32() == (int)model.ActiveTab;
            button.SetPressedNoSignal(selected);
            button.ThemeTypeVariation = selected ? "" : Ui.ButtonGhost;
        }

        StopMicTest();
        Ui.Clear(_pane);
        _micLevel = null;
        _micTestButton = null;
        _micTestStatus = null;
        SetProcess(false);
        var draft = model.Draft;
        var tabKey = TabKeys.First(t => t.Tab == model.ActiveTab).Key;
        _pane.AddChild(Ui.Label(model.ActiveTab == SettingsTab.VoiceAndPrivacy ? model.PrivacyTitle : text.Get(tabKey), Ui.H1));

        switch (model.ActiveTab)
        {
            case SettingsTab.Audio:
                var audio = draft.Audio;
                VolumeRow("settings.audioMaster", null, audio.MasterLinear, v => s => s with { Audio = s.Audio with { MasterLinear = v } });
                VolumeRow("settings.audioSpeech", "settings.audioSpeechHint", audio.SpeechLinear, v => s => s with { Audio = s.Audio with { SpeechLinear = v } });
                VolumeRow("settings.audioAlarms", "settings.audioAlarmsHint", audio.AlarmsLinear, v => s => s with { Audio = s.Audio with { AlarmsLinear = v } });
                VolumeRow("settings.audioSfx", null, audio.SfxLinear, v => s => s with { Audio = s.Audio with { SfxLinear = v } });
                VolumeRow("settings.audioAmbience", "settings.audioAmbienceHint", audio.AmbienceLinear, v => s => s with { Audio = s.Audio with { AmbienceLinear = v } });
                VolumeRow("settings.audioUi", null, audio.UiLinear, v => s => s with { Audio = s.Audio with { UiLinear = v } });
                break;
            case SettingsTab.Captions:
                BuildCaptions(draft.Captions, text, tokens);
                break;
            case SettingsTab.Display:
                BuildDisplay(draft.Display, text);
                break;
            case SettingsTab.Controls:
                BuildControls(model, text);
                break;
            case SettingsTab.VoiceAndPrivacy:
                BuildVoice(model, draft, text, tokens);
                break;
            case SettingsTab.Gameplay:
                AddRow(text.Get("settings.tutorialCallouts"), text.Get("settings.tutorialCalloutsHint"),
                    Toggle(draft.Gameplay.TutorialCallouts, on => Edit(s => s with { Gameplay = s.Gameplay with { TutorialCallouts = on } }, now: true)));
                var locked = Ui.IconLabel(Ui.IconLocked, tokens.ColorTextLo, model.TipFrequencyNote, Ui.Small);
                AddRow(text.Get("settings.tipFrequency"), text.Get("settings.tipFrequencyHint"), locked);
                break;
        }
        ShowProblems();
    }

    private void BuildCaptions(CaptionSettings captions, FrontendText text, UiTokenSet tokens)
    {
        AddRow(text.Get("settings.captionsEnabled"), text.Get("settings.captionsEnabledHint"),
            Toggle(captions.Enabled, on => { Edit(s => s with { Captions = s.Captions with { Enabled = on } }, now: true); BuildPane(); }));
        var size = Slider(CaptionSettings.MinSizePx, CaptionSettings.MaxSizePx, 1, captions.SizePx, v => SettingsText.Px(SettingsText.CaptionSize(v)),
            v => Edit(s => s with { Captions = s.Captions with { SizePx = SettingsText.CaptionSize(v) } }, now: false));
        AddRow(text.Get("settings.captionSize"), text.Get("settings.captionSizeHint"), size);
        var opacity = Slider(0, 100, 5, SettingsText.PercentOf(captions.BackgroundOpacity), v => SettingsText.Percent(SettingsText.LinearOf(v)),
            v => Edit(s => s with { Captions = s.Captions with { BackgroundOpacity = SettingsText.LinearOf(v) } }, now: false));
        AddRow(text.Get("settings.captionOpacity"), text.Get("settings.captionOpacityHint"), opacity);
        AddRow(text.Get("settings.captionsSpeakerNames"), null,
            Toggle(captions.ShowSpeakerNames, on => Edit(s => s with { Captions = s.Captions with { ShowSpeakerNames = on } }, now: true)));

        _pane.AddChild(Ui.Label(text.Get("settings.captionPreviewTitle").ToUpperInvariant(), Ui.Meta));
        var line = Ui.Row(8);
        if (captions.ShowSpeakerNames)
        {
            var speaker = Ui.Label(text.Get("settings.captionPreviewSpeaker"), Ui.Caption);
            speaker.AddThemeFontSizeOverride("font_size", captions.SizePx);
            speaker.AddThemeColorOverride("font_color", tokens.ColorTextMid);
            line.AddChild(speaker);
        }
        var caption = Ui.Label(text.Get("settings.captionPreviewLine"), Ui.Caption, wrap: true);
        caption.AddThemeFontSizeOverride("font_size", captions.SizePx);
        line.AddChild(caption);
        var box = Ui.Panel(line, 12);
        var background = tokens.ColorBg0;
        background.A = captions.BackgroundOpacity;
        box.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = background });
        _pane.AddChild(box);
    }

    private void BuildDisplay(DisplaySettings display, FrontendText text)
    {
        var scale = Slider(DisplaySettings.MinUiScale * 100, DisplaySettings.MaxUiScale * 100, SettingsText.UiScaleStepPercent,
            SettingsText.UiScalePercent(display.UiScale), v => SettingsText.UiScale(SettingsText.UiScaleOf(v)),
            v => Edit(s => s with { Display = s.Display with { UiScale = SettingsText.UiScaleOf(v) } }, now: false));
        AddRow(text.Get("settings.uiScale"), text.Get("settings.uiScaleHint"), scale);
        AddRow(text.Get("settings.highContrast"), text.Get("settings.highContrastHint"),
            Toggle(display.HighContrast, on => Edit(s => s with { Display = s.Display with { HighContrast = on } }, now: true)));
        AddRow(text.Get("settings.reducedMotion"), text.Get("settings.reducedMotionHint"),
            Toggle(display.ReducedMotion, on => Edit(s => s with { Display = s.Display with { ReducedMotion = on } }, now: true)));

        var modes = new OptionButton { CustomMinimumSize = new Vector2(240, 36) };
        foreach (var mode in Enum.GetValues<GameWindowMode>()) modes.AddItem(text.Get(SettingsText.WindowModeKey(mode)), (int)mode);
        modes.Selected = modes.GetItemIndex((int)display.WindowMode);
        modes.ItemSelected += index =>
        {
            var mode = (GameWindowMode)modes.GetItemId((int)index);
            Edit(s => s with { Display = s.Display with { WindowMode = mode } }, now: true);
        };
        AddRow(text.Get("settings.windowMode"), null, modes);
    }

    private void BuildControls(SettingsModel model, FrontendText text)
    {
        foreach (var action in model.Actions)
        {
            var capturing = model.Capture is { } capture && capture.Action == action ? capture : null;
            var caps = Ui.Row(8);
            var bindings = model.EffectiveBindings(action);
            for (var slot = 0; slot < bindings.Count; slot++)
            {
                var target = slot;
                var label = capturing?.Slot == slot ? text.Get("settings.pressKey") : BindingText.Of(bindings[slot]);
                var cap = Ui.Button(label, capturing?.Slot == slot ? Ui.ButtonPrimary : "", () => BeginCapture(action, target));
                cap.AddThemeFontOverride("font", GetThemeFont("font", Ui.ReadoutSmall));
                caps.AddChild(cap);
            }
            var rebind = capturing is not null
                ? Ui.Button(text.Get("settings.cancelRebind"), Ui.ButtonGhost, () => { _model?.CancelCapture(); _captureMessage = null; BuildPane(); })
                : Ui.Button(text.Get("settings.rebind"), Ui.ButtonGhost, () => BeginCapture(action, 0));
            caps.AddChild(rebind);
            AddRow(ActionLabel(action), null, caps);
            if (capturing is not null && _builtFromCapture) rebind.CallDeferred(Control.MethodName.GrabFocus);
        }
        _pane.AddChild(Ui.Label(text.Get("settings.controlsNote"), Ui.Meta, wrap: true));
        _builtFromCapture = false;
    }

    private void BuildVoice(SettingsModel model, GameSettings draft, FrontendText text, UiTokenSet tokens)
    {
        AddRow(text.Get("settings.microphone"), (draft.Voice.MicEnabled ? model.MicOnLabel : model.MicOffLabel) + ". " + text.Get("settings.microphoneHint"),
            Toggle(draft.Voice.MicEnabled, on => { Edit(s => s with { Voice = s.Voice with { MicEnabled = on } }, now: true); BuildPane(); }));

        var devices = new OptionButton { CustomMinimumSize = new Vector2(320, 36) };
        var names = AudioServer.GetInputDeviceList();
        if (!names.Contains(draft.Voice.InputDevice)) devices.AddItem(draft.Voice.InputDevice);
        foreach (var name in names) devices.AddItem(name);
        for (var i = 0; i < devices.ItemCount; i++)
        {
            if (devices.GetItemText(i) == draft.Voice.InputDevice) devices.Selected = i;
        }
        devices.ItemSelected += index =>
        {
            var device = devices.GetItemText((int)index);
            Edit(s => s with { Voice = s.Voice with { InputDevice = device } }, now: true);
        };
        AddRow(model.InputDeviceLabel, text.Get("settings.inputDeviceHint"), devices);

        _micLevel = Ui.Bar(0, tokens.ColorOk, tokens, 12);
        _micLevel.CustomMinimumSize = new Vector2(200, 12);
        var meter = Ui.Row(12);
        meter.AddChild(_micLevel);
        _micTestButton = Ui.Button(text.Get("settings.micTest"), Ui.ButtonGhost, StartMicTest);
        meter.AddChild(_micTestButton);
        AddRow(model.MicOnLabel, text.Get("settings.micTestHint"), meter);
        _micTestStatus = Ui.Label("", Ui.Small, wrap: true);
        _pane.AddChild(_micTestStatus);
        ShowMicTest();
        // A switched-off microphone cannot be tested; the toggle above rebuilds the pane.
        if (!draft.Voice.MicEnabled) _micTestButton.Disabled = true;
        SetProcess(true);

        var ptt = Ui.Row(8);
        foreach (var binding in model.EffectiveBindings(SettingsModel.PushToTalkAction)) ptt.AddChild(Ui.KeyCap(BindingText.Of(binding)));
        AddRow(text.Get("settings.pushToTalkKey"), text.Get("settings.pushToTalkHint"), ptt);

        var url = new LineEdit { Text = draft.BackendUrl, CustomMinimumSize = new Vector2(320, 36) };
        url.TextSubmitted += value => Edit(s => s with { BackendUrl = value.Trim() }, now: true);
        AddRow(text.Get("settings.backendUrl"), text.Get("settings.backendUrlHint"), url);

        // Unset means the build default; the first toggle stores the player's choice. Read at the next start.
        var autoStart = draft.BackendAutoStart
            ?? BackendLaunchOptions.ResolveAutoStart(OS.IsDebugBuild(), System.Environment.GetEnvironmentVariable(BackendLaunchOptions.AutoStartVariable));
        AddRow(text.Get("settings.backendAutoStart"), text.Get("settings.backendAutoStartHint"),
            Toggle(autoStart, on => Edit(s => s with { BackendAutoStart = on }, now: true)));

        var notice = Ui.Row(12);
        notice.AddChild(Ui.Icon("res://assets/ui/icons/state/icon_state_info.svg", tokens.ColorInfo));
        var words = Ui.Column(4);
        words.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        words.AddChild(Ui.Label(model.PrivacyTitle.ToUpperInvariant(), Ui.H2));
        words.AddChild(Ui.Label(model.PrivacyNotice, "", wrap: true));
        notice.AddChild(words);
        var box = Ui.Panel(notice, 16);
        box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = tokens.ColorTintInfo,
            BorderColor = tokens.ColorInfo,
            BorderWidthLeft = tokens.BorderWidthPx,
            BorderWidthTop = tokens.BorderWidthPx,
            BorderWidthRight = tokens.BorderWidthPx,
            BorderWidthBottom = tokens.BorderWidthPx,
            CornerRadiusTopLeft = tokens.RadiusControlPx,
            CornerRadiusTopRight = tokens.RadiusControlPx,
            CornerRadiusBottomLeft = tokens.RadiusControlPx,
            CornerRadiusBottomRight = tokens.RadiusControlPx,
        });
        _pane.AddChild(box);
    }

    private void BeginCapture(string action, int slot)
    {
        if (_model is null) return;
        _captureMessage = null;
        _model.BeginCapture(action, slot);
        _builtFromCapture = true;
        BuildPane();
    }

    private void VolumeRow(string labelKey, string? hintKey, float value, Func<float, Func<GameSettings, GameSettings>> change)
    {
        if (_context is not { } context) return;
        var slider = Slider(0, 100, 1, SettingsText.PercentOf(value), v => SettingsText.Percent(SettingsText.LinearOf(v)),
            v => Edit(change(SettingsText.LinearOf(v)), now: false));
        AddRow(context.Text.Get(labelKey), hintKey is null ? null : context.Text.Get(hintKey), slider);
    }

    private static HBoxContainer Slider(double min, double max, double step, double value, Func<double, string> format, Action<double> changed)
    {
        var row = Ui.Row(16);
        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value,
            CustomMinimumSize = new Vector2(200, 24),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            FocusMode = FocusModeEnum.All,
        };
        var readout = Ui.Label(format(value), Ui.ReadoutSmall);
        readout.CustomMinimumSize = new Vector2(64, 0);
        readout.HorizontalAlignment = HorizontalAlignment.Right;
        slider.ValueChanged += v =>
        {
            readout.Text = format(v);
            changed(v);
        };
        row.AddChild(slider);
        row.AddChild(readout);
        return row;
    }

    private HBoxContainer Toggle(bool on, Action<bool> toggled)
    {
        var row = Ui.Row(8);
        var check = new CheckButton { ButtonPressed = on, FocusMode = FocusModeEnum.All };
        var state = Ui.Label(StateWord(on), Ui.Meta);
        state.VerticalAlignment = VerticalAlignment.Center;
        check.Toggled += value =>
        {
            state.Text = StateWord(value);
            toggled(value);
        };
        row.AddChild(check);
        row.AddChild(state);
        return row;
    }

    private string StateWord(bool on) => _context?.Text.Get(on ? "settings.on" : "settings.off") ?? string.Empty;

    private string ActionLabel(string action) => _context?.Text.GetOr($"settings.action.{action}", action) ?? action;

    private void AddRow(string label, string? hint, Control control)
    {
        if (_context is null) return;
        var row = Ui.Row(24);
        var words = Ui.Column(2);
        words.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        words.AddChild(Ui.Label(label));
        if (!string.IsNullOrEmpty(hint)) words.AddChild(Ui.Label(hint, Ui.Small, wrap: true));
        row.AddChild(words);
        control.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(control);
        _pane.AddChild(row);
        _pane.AddChild(Ui.Divider(_context.Tokens));
    }
}
