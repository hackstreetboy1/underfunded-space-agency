namespace Usa.Simulation.Knowledge.Ledger;

/// <summary>The ledger's full answer: total mass band, design dry band and each tank's propellant band.</summary>
/// <param name="Tanks">One entry per tank in spec order, including detached ones.</param>
public sealed record GroundMassEstimate(MassBand Mass, MassBand Dry, IReadOnlyList<TankEstimate> Tanks);

/// <summary>What the ground knows of one tank's propellant.</summary>
/// <param name="Attached">False once its section was jettisoned; the band is then zero and not counted.</param>
public sealed record TankEstimate(string TankId, MassBand Propellant, TankKnowledgeBasis Basis, GaugeBiasTreatment Bias, bool Attached);

/// <summary>Which knowledge a tank's band rests on (X8: a reading, a downlink band, or unknown).</summary>
public enum TankKnowledgeBasis
{
    /// <summary>Nothing usable: 0 to capacity, less known draws.</summary>
    Unknown,

    /// <summary>A gauge reading (crew read or telemetry), plus or minus the reading tolerance.</summary>
    Reading,

    /// <summary>A coarse downlink word mapped to its band limits.</summary>
    GroundBand,
}

/// <summary>How the ledger treated the gauge's bias.</summary>
public enum GaugeBiasTreatment
{
    /// <summary>No bias known to the ground; an undiagnosed bias passes through uncorrected.</summary>
    None,

    /// <summary>A confirmed bias was subtracted and the band says so.</summary>
    Corrected,

    /// <summary>A suspected bias widened the band by the authored magnitude on both sides.</summary>
    Widened,

    /// <summary>The gauge is confirmed failed (or biased by an unknown amount): its readings are ignored.</summary>
    GaugeFailed,
}
