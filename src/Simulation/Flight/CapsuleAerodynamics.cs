namespace Usa.Simulation.Flight;

/// <summary>Ballistic coefficients β = m / (Cd A) for each descent configuration.</summary>
public sealed record CapsuleAerodynamics(double BallisticCoefficientKgPerM2, ChuteSpec Drogue, ChuteSpec Main)
{
    public ChuteSpec Chute(ChuteKind kind) => kind == ChuteKind.Drogue ? Drogue : Main;
}

/// <param name="BallisticCoefficientKgPerM2">β with this chute open.</param>
/// <param name="NominalDeployAltitudeM">Altitude the entry checklist (and previews) deploy at.</param>
/// <param name="MaxDeployAltitudeM">Above this the chute is lost when deployed.</param>
/// <param name="MaxDeploySpeedMps">Above this airspeed the chute is torn when deployed.</param>
public sealed record ChuteSpec(
    double BallisticCoefficientKgPerM2,
    double NominalDeployAltitudeM,
    double MaxDeployAltitudeM,
    double MaxDeploySpeedMps)
{
    /// <summary>Whether opening the chute at this altitude and airspeed works or loses it.</summary>
    public ChuteDeployResult Evaluate(double altitudeM, double airspeedMps) =>
        altitudeM > MaxDeployAltitudeM ? ChuteDeployResult.TooHigh
        : airspeedMps > MaxDeploySpeedMps ? ChuteDeployResult.TooFast
        : ChuteDeployResult.Deployed;
}

public enum ChuteKind
{
    Drogue,
    Main,
}

public enum ChuteState
{
    Stowed,
    Deployed,
    Failed,
}

public enum ChuteDeployResult
{
    Deployed,
    AlreadyDeployed,
    AlreadyFailed,
    TooHigh,
    TooFast,
    Landed,
}
