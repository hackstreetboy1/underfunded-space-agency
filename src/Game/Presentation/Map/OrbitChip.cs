namespace Usa.Game.Presentation.Map;

/// <summary>
/// The orbit chip of the map (Figma G4-01: <c>Pe 290 km · Ap 305 km · T 90:19</c>), from the ground's estimated conic,
/// with the fix meta line and its state badge.
/// </summary>
/// <param name="ApsidesText"><c>Pe 290 km · Ap 305 km</c>, or <c>Pe 120 km · Ap escape</c> for an escape path.</param>
/// <param name="PeriodS">Orbital period of the estimated conic; null for an escape path.</param>
/// <param name="PeriodText"><c>T 90:19</c>; null for an escape path.</param>
/// <param name="Text">The chip line: apsides and period.</param>
/// <param name="Meta"><c>ground tracking, Pacific Island · MET 06:58</c>, with <c>EST</c> or <c>STALE 34m</c>; null without a fix.</param>
/// <param name="Badge"><c>EST</c>, <c>STALE 34m</c>, or empty for a fresh fix.</param>
public sealed record OrbitChip(
    string ApsidesText,
    double? PeriodS,
    string? PeriodText,
    string Text,
    string? Meta,
    OrbitFixState State,
    string Badge);
