using Godot;
using Usa.Game.Presentation.Workspace;
using Usa.Game.UI.Console;
using Usa.Game.UI.Entry;

namespace Usa.Game.UI.Workspace;

/// <summary>
/// One astronaut in the assignee picker (<c>AssigneeOption.tscn</c>, Figma "Assignee option" Default / Selected and
/// "Likelihood band" in node 36:242): crew icon, name, predicted duration in MET and the likelihood band (shape icon and
/// word, never a %).
/// </summary>
public sealed partial class AssigneeOptionView : Button
{
    private readonly VariationSetter _variations = new();
    private Label _name = null!;
    private Label _detail = null!;
    private TextureRect _avatar = null!;
    private PanelContainer _band = null!;
    private TextureRect _bandIcon = null!;
    private Label _bandText = null!;

    public AssigneeOption? Option { get; private set; }

    public override void _Ready()
    {
        _name = GetNode<Label>("%Name");
        _detail = GetNode<Label>("%Detail");
        _avatar = GetNode<TextureRect>("%Avatar");
        _band = GetNode<PanelContainer>("%Band");
        _bandIcon = GetNode<TextureRect>("%Band/Row/Icon");
        _bandText = GetNode<Label>("%Band/Row/Text");
    }

    public void Show(AssigneeOption option, WorkspaceWords words, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(palette);
        Option = option;
        _name.Text = option.Name;
        _detail.Text = option.Available
            ? WorkspaceWords.Fill(words.OptionDuration, ("duration", option.PredictedDurationText))
            : option.UnavailableReason;
        Disabled = !option.Available;
        TooltipText = option.Available ? string.Empty : option.UnavailableReason;
        _avatar.SelfModulate = palette.Of("text.mid");
        var (shape, token) = option.Likelihood switch
        {
            LikelihoodBand.High => (HudShape.Nominal, "ok"),
            LikelihoodBand.Medium => (HudShape.Caution, "caution"),
            _ => (HudShape.Failed, "critical"),
        };
        _variations.SetBadge(_band, _bandText, token);
        HudIcons.Show(_bandIcon, shape, palette.Of(token));
        _bandText.Text = option.LikelihoodText.ToUpperInvariant();
    }
}
