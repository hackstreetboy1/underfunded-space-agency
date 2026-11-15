using Godot;
using Usa.Game.Presentation.Console.Resources;

namespace Usa.Game.UI.Console.Components;

/// <summary>A mini health bar on the resource strip's Crew tile (<c>CrewHealthMini.tscn</c>, Figma G1-03 node 12:393).</summary>
public sealed partial class CrewHealthMini : HBoxContainer
{
    private Label _name = null!;
    private ProgressBar _bar = null!;
    private Label _percent = null!;

    public override void _Ready()
    {
        _name = GetNode<Label>("%Name");
        _bar = GetNode<ProgressBar>("%Bar");
        _percent = GetNode<Label>("%Percent");
    }

    public void Bind(CrewHealthBar health)
    {
        ArgumentNullException.ThrowIfNull(health);
        _name.Text = health.Name;
        _bar.Value = (health.Health.Fraction ?? 0) * _bar.MaxValue;
        _percent.Text = health.Health.PercentText;
    }
}
