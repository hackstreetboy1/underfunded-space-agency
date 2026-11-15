using Godot;
using Usa.Game.UI.Console.Binding;

namespace Usa.Game.UI.Console.Components;

/// <summary>
/// Panel header (<c>PanelHeader.tscn</c>, Figma G1-02 "Panel header" Standard / Legacy): caps title on the left, a small
/// aside (shortcut hint, selected tab) on the right. Legacy is the older Program 2 style.
/// </summary>
public sealed partial class PanelHeader : PanelContainer
{
    private Label _title = null!;
    private Label _aside = null!;
    private string _titleText = string.Empty;
    private string _asideText = string.Empty;
    private bool _legacy;

    [Export]
    public string Title
    {
        get => _titleText;
        set
        {
            _titleText = value ?? string.Empty;
            if (IsNodeReady()) _title.Text = _titleText;
        }
    }

    [Export]
    public string Aside
    {
        get => _asideText;
        set
        {
            _asideText = value ?? string.Empty;
            if (IsNodeReady()) _aside.ShowText(_asideText);
        }
    }

    [Export]
    public bool Legacy
    {
        get => _legacy;
        set
        {
            _legacy = value;
            if (IsNodeReady()) ApplyStyle();
        }
    }

    public override void _Ready()
    {
        _title = GetNode<Label>("%Title");
        _aside = GetNode<Label>("%Aside");
        _title.Text = _titleText;
        _aside.ShowText(_asideText);
        ApplyStyle();
    }

    private void ApplyStyle()
    {
        this.SetVariation(ThemeVariations.PanelHeaderFor(_legacy));
        _title.SetVariation(ThemeVariations.PanelHeaderTitleFor(_legacy));
    }
}
