using Godot;
using Usa.Game.Flow.Screens;
using Usa.Game.UI.Frontend.Model;

namespace Usa.Game.UI.Frontend;

/// <summary>
/// The ground-link line of the main menu, the briefing and the debrief (GDD 15.6): status glyph and headline, the one-line
/// how-to, Retry when it helps (<c>GameServices.Backend.Retry()</c> through <see cref="BackendStatusModel"/>) and, while a
/// mission may not start, the "mission waits" line. Built in code; <see cref="Bind"/> once, the screen reads
/// <see cref="MissionMayStart"/> and listens to <see cref="Changed"/> to enable its launch buttons.
/// </summary>
public sealed partial class BackendStatusPanel : VBoxContainer
{
    private FrontendContext? _context;
    private BackendStatusModel? _model;

    /// <summary>Raised after every status change, once the panel shows it.</summary>
    public event Action? Changed;

    /// <summary>True without a tracker (tests, tools): nothing then holds the mission back.</summary>
    public bool MissionMayStart => _model?.MissionMayStart ?? true;

    // Detached on deletion, not on leaving the tree: a screen may move the panel while it rebuilds around it.
    public override void _Notification(int what)
    {
        if (what != NotificationPredelete || _model is null) return;
        _model.Changed -= Render;
        _model.Detach();
        _model = null;
    }

    /// <returns>False when the services carry no backend tracker; the panel then stays hidden.</returns>
    public bool Bind(FrontendContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        AddThemeConstantOverride("separation", 4);
        if (context.Services.Backend is not { } tracker)
        {
            Visible = false;
            return false;
        }
        _model = new BackendStatusModel(tracker, context.Strings);
        _model.Changed += Render;
        Render();
        return true;
    }

    private void Render()
    {
        if (_model is not { } model || _context is not { } context) return;
        var tokens = context.Tokens;
        Ui.Clear(this);

        var head = Ui.Row(8);
        var tone = BackendStatusTone.Of(model.State);
        var glyph = StatusGlyph.Create(tone, Ui.ToneColour(tone, tokens));
        glyph.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        head.AddChild(glyph);
        var headline = Ui.Label(model.Headline, Ui.Small, wrap: true);
        headline.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(headline);
        if (model.ShowRetry) head.AddChild(Ui.Button(model.RetryLabel, Ui.ButtonGhost, () => _model?.Retry()));
        AddChild(head);

        if (model.Detail is { } detail) AddChild(Ui.Label(detail, Ui.Meta, wrap: true));
        if (!model.MissionMayStart) AddChild(Ui.Label(model.MissionWaitsLine, Ui.Meta, wrap: true));
        Changed?.Invoke();
    }
}
