namespace Usa.Simulation.Attitude;

/// <summary>The state-path and effect text for <see cref="AttitudeMode"/>, matching the data enum strings.</summary>
public static class AttitudeModeText
{
    public const string Off = "off";
    public const string RateDamp = "rate_damp";
    public const string AttitudeHold = "attitude_hold";

    public static string ToText(AttitudeMode mode) => mode switch
    {
        AttitudeMode.RateDamp => RateDamp,
        AttitudeMode.AttitudeHold => AttitudeHold,
        _ => Off,
    };

    public static bool TryParse(string text, out AttitudeMode mode)
    {
        switch (text)
        {
            case Off: mode = AttitudeMode.Off; return true;
            case RateDamp: mode = AttitudeMode.RateDamp; return true;
            case AttitudeHold: mode = AttitudeMode.AttitudeHold; return true;
            default: mode = AttitudeMode.Off; return false;
        }
    }
}
