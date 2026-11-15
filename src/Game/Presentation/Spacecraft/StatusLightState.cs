namespace Usa.Game.Presentation.Spacecraft;

/// <summary>What one Lodestar status light shows. Unknown is drawn dark: the panel never shows more than the player knows.</summary>
public enum StatusLightState
{
    Unknown,
    Off,
    On,
    Blinking,
}
