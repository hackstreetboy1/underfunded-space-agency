namespace Usa.Game.Presentation.Console.Status;

/// <summary>How a status is drawn: always a shape and a word, never colour alone.</summary>
/// <param name="Glyph">Text fallback for <paramref name="Shape"/>, e.g. <c>▲</c>.</param>
/// <param name="Word">Caps word, e.g. <c>CAUTION</c>.</param>
/// <param name="ColourToken">A key from <see cref="ColourTokens"/>.</param>
/// <param name="Outlined">Draw the shape as a (dashed) outline, as for suspected and unknown.</param>
public sealed record StatusVisual(StatusShape Shape, string Glyph, string Word, string ColourToken, bool Outlined = false);
