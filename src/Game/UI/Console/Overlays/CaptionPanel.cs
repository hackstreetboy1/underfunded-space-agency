using Godot;
using Usa.Game.Services.Settings;
using Usa.Game.UI.Console.Binding;

namespace Usa.Game.UI.Console.Overlays;

/// <summary>
/// Crew captions (<c>CaptionPanel.tscn</c>, UI.md 2.4 Caption, 7): the speaker's name and the line on a <c>bg.0</c> backing,
/// at the settings' size and opacity, held for each cue's time (the hold pauses while the game is frozen).
/// </summary>
public sealed partial class CaptionPanel : PanelContainer
{
    private static readonly StringName FontSize = "font_size";

    private readonly BindingScope _bindings = new();
    private Label _speaker = null!;
    private Label _text = null!;
    private ConsolePalette _palette = ConsolePalette.Default;
    private CaptionSettings _settings = new();

    public override void _Ready()
    {
        _speaker = GetNode<Label>("%Speaker");
        _text = GetNode<Label>("%Text");
        Visible = false;
        ApplySettings(_settings);
    }

    public override void _ExitTree() => Unbind();

    public void Bind(CaptionHold captions, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(captions);
        ArgumentNullException.ThrowIfNull(palette);
        Unbind();
        _palette = palette;
        ApplySettings(_settings);
        _bindings.Observe(captions.Current, line =>
        {
            // Captions off in settings: cues are dropped, nothing shows.
            Visible = line is not null && _settings.Enabled;
            if (line is null || !_settings.Enabled) return;
            _speaker.ShowText(_settings.ShowSpeakerNames ? line.Speaker : null);
            _text.Text = line.Text;
        });
    }

    public void Unbind()
    {
        _bindings.Release();
        Visible = false;
    }

    /// <summary>Caption size (18–32 px), backing opacity and speaker names from the settings.</summary>
    public void ApplySettings(CaptionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        if (!IsNodeReady()) return;
        var size = Math.Clamp(settings.SizePx, CaptionSettings.MinSizePx, CaptionSettings.MaxSizePx);
        _speaker.AddThemeFontSizeOverride(FontSize, size);
        _text.AddThemeFontSizeOverride(FontSize, size);
        AddThemeStyleboxOverride("panel", ConsoleStyles.CaptionBacking(_palette, settings.BackgroundOpacity));
        if (!settings.ShowSpeakerNames) _speaker.Visible = false;
        if (!settings.Enabled) Visible = false;
    }
}
