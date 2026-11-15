using Godot;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.UI.Console.Binding;

namespace Usa.Game.UI.Console.Components;

/// <summary>
/// A status or knowledge badge (<c>Badge.tscn</c>, Figma G1-02 "Status badge" / "Knowledge badge"): the shape as an approved
/// icon (review R3-11; a drawn ring for the hollow circle) and the word, on the tone's badge style. Hidden while it has no word.
/// </summary>
public sealed partial class BadgeView : PanelContainer
{
    private static readonly StringName FontColour = "font_color";

    private Label _text = null!;
    private StatusIconView _icon = null!;

    public override void _Ready()
    {
        _text = GetNode<Label>("%Text");
        var row = GetNode<HBoxContainer>("%Row");
        _icon = StatusIconView.Create(12);
        row.AddChild(_icon);
        row.MoveChild(_icon, 0);
        _icon.Visible = false;
    }

    /// <param name="colourToken">A <c>ColourTokens</c> key; null or a text tone gives the neutral badge.</param>
    public void Display(string text, string? colourToken) => Display(text, colourToken, StatusShape.None);

    public void Display(StatusVisual status)
    {
        ArgumentNullException.ThrowIfNull(status);
        Display(status.Word, status.ColourToken, status.Shape);
    }

    public void Display(KnowledgeVisual knowledge, string? badge)
    {
        ArgumentNullException.ThrowIfNull(knowledge);
        var word = badge ?? string.Empty;
        // The formatter writes some badges with their glyph (⇄ CONFLICT); the icon replaces it.
        if (knowledge.Glyph.Length > 0 && word.StartsWith(knowledge.Glyph, StringComparison.Ordinal)) word = word[knowledge.Glyph.Length..].TrimStart();
        Display(word, knowledge.BadgeColourToken, word.Length > 0 ? knowledge.Icon : StatusShape.None);
    }

    /// <summary>A word with an explicit shape, e.g. the header's <c>1 CAUTION</c> count.</summary>
    public void Display(string text, string? colourToken, StatusShape shape)
    {
        Visible = text.Length > 0;
        this.SetVariation(ThemeVariations.BadgePanel(colourToken));
        _text.SetVariation(ThemeVariations.BadgeText(colourToken));
        _text.Text = text;
        _icon.Show(shape, _text.GetThemeColor(FontColour));
    }
}
