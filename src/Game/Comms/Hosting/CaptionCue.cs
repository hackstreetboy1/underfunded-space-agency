using Usa.Game.Services.Voice;

namespace Usa.Game.Comms.Hosting;

/// <summary>A caption to show now and keep up for <paramref name="HoldSeconds"/>.</summary>
/// <param name="HoldSeconds">
/// From the line's start: its clip length (or reading time when caption-only) plus the 1.5 s tail. Time spent paused or
/// frozen does not count, so the panel's countdown should hold while the game is frozen.
/// </param>
public sealed record CaptionCue(SpeechLine Line, double HoldSeconds);
