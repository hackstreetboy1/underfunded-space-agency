namespace Usa.Game.Presentation.Console;

/// <summary>A pre-filled crew request from an unknown value's "Ask" affordance (UI.md 2.3).</summary>
/// <param name="Key">Fact or tile key the request is about.</param>
/// <param name="Text">Text placed in the comms input for review, e.g. <c>Read OAMS</c>.</param>
public sealed record AskDraft(string Key, string Label, string Text);
