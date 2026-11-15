using Godot;
using Usa.Game.Integration.Runtime;

namespace Usa.Game.UI.Console.Overlays;

/// <summary>
/// The crew-lost closing card (<c>ClosingCard.tscn</c>; integration bind contract 3, UI.md 3.4 deceased style): centred over
/// the main view, quiet (<c>text.mid</c> on <c>bg.2</c>, no red, no alarm tone), title, body, note and one button that asks
/// for the debrief. A recovered mission closes on the recovery card instead.
/// </summary>
public sealed partial class ClosingCard : PanelContainer
{
    private Label _title = null!;
    private Label _body = null!;
    private Label _note = null!;
    private Button _proceed = null!;

    /// <summary>"Proceed to debrief".</summary>
    public event Action? ProceedRequested;

    public override void _Ready()
    {
        _title = GetNode<Label>("%Title");
        _body = GetNode<Label>("%Body");
        _note = GetNode<Label>("%Note");
        _proceed = GetNode<Button>("%Proceed");
        _proceed.Pressed += () => ProceedRequested?.Invoke();
        Visible = false;
    }

    public void Show(MissionClosingCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        _title.Text = card.Title;
        _body.Text = card.Body;
        _note.Text = card.Note;
        _proceed.Text = MissionClosingCard.ProceedLabel;
        Visible = true;
        _proceed.GrabFocus();
    }
}
