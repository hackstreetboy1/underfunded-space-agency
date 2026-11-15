using Godot;
using Usa.Game.Presentation.Console.Resources;
using Usa.Game.UI.Console.Binding;
using Usa.Game.UI.Console.Components;

namespace Usa.Game.UI.Console.Overlays;

/// <summary>
/// The forecast breakdown popover (<c>ForecastBreakdown.tscn</c>; UI.md 3.2 "click → forecast breakdown popover with
/// assumptions", GDD 6.4): the headline, when it was computed, confidence with its reason, whether it covers the planned
/// return, one row per resource (range, STALE / UNKNOWN input, LIMIT on the one that sets the autonomy) and the assumptions.
/// Always a range and a confidence, never a single countdown.
/// </summary>
public sealed partial class ForecastBreakdownPopup : PopupPanel
{
    private Label _title = null!;
    private Label _headline = null!;
    private Label _computedAt = null!;
    private Label _confidence = null!;
    private BadgeView _coverage = null!;
    private Label _coverageText = null!;
    private VBoxContainer _rows = null!;
    private Label _assumptionsCaption = null!;
    private VBoxContainer _assumptions = null!;
    private Button _close = null!;
    private string _limitingBadge = "LIMIT";

    /// <summary>The popover closed (Close, Esc or a click outside).</summary>
    public event Action? Closed;

    public override void _Ready()
    {
        _title = GetNode<Label>("%Title");
        _headline = GetNode<Label>("%Headline");
        _computedAt = GetNode<Label>("%ComputedAt");
        _confidence = GetNode<Label>("%Confidence");
        _coverage = GetNode<BadgeView>("%Coverage");
        _coverageText = GetNode<Label>("%CoverageText");
        _rows = GetNode<VBoxContainer>("%ResourceRows");
        _assumptionsCaption = GetNode<Label>("%AssumptionsCaption");
        _assumptions = GetNode<VBoxContainer>("%Assumptions");
        _close = GetNode<Button>("%Close");
        _close.Pressed += Hide;
        PopupHide += () => Closed?.Invoke();
    }

    public void ApplyLabels(ConsoleLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        _title.Text = labels.ForecastBreakdownTitle;
        _assumptionsCaption.Text = labels.ForecastAssumptions;
        _close.Text = labels.ForecastClose;
        _limitingBadge = labels.ForecastLimitingBadge;
    }

    public void Display(ForecastBreakdown breakdown)
    {
        ArgumentNullException.ThrowIfNull(breakdown);
        _headline.Text = breakdown.Headline;
        _computedAt.Text = breakdown.ComputedAtText;
        _confidence.Text = breakdown.ConfidenceText;
        _coverage.Display(breakdown.Coverage);
        _coverageText.Text = breakdown.CoverageText;

        Clear(_rows);
        foreach (var row in breakdown.Rows)
        {
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            line.AddChild(new Label { Text = row.Label, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            line.AddChild(new Label { Text = row.RangeText, ThemeTypeVariation = ThemeVariations.ReadoutSmall });
            if (row.Badge is { } badge) line.AddChild(new Label { Text = badge, ThemeTypeVariation = ThemeVariations.BadgeLabel });
            if (row.Limiting) line.AddChild(new Label { Text = _limitingBadge, ThemeTypeVariation = ThemeVariations.BadgeText("accent") });
            _rows.AddChild(line);
        }

        Clear(_assumptions);
        foreach (var assumption in breakdown.Assumptions)
            _assumptions.AddChild(new Label { Text = "· " + assumption, ThemeTypeVariation = ThemeVariations.Small, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _assumptionsCaption.Visible = breakdown.Assumptions.Count > 0;
    }

    private static void Clear(Node container)
    {
        foreach (var child in container.GetChildren())
        {
            container.RemoveChild(child);
            child.QueueFree();
        }
    }
}
