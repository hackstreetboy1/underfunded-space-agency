using Usa.Simulation.Flight;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Game.Presentation.Map;

/// <summary>Why a drawn arc stops.</summary>
public enum ArcEnd
{
    /// <summary>A full closed ellipse.</summary>
    Closed,

    /// <summary>The path meets the body's surface.</summary>
    Impact,

    /// <summary>A hyperbolic path leaves the visible area.</summary>
    ViewEdge,

    /// <summary>Nothing of the path is drawable.</summary>
    Empty,
}

/// <summary>
/// The drawable true-anomaly range of a conic, with anomalies continuous across the range (an elliptic arc
/// through apoapsis runs from ν to 2π - ν, not across the ±π wrap).
/// </summary>
public readonly record struct ConicArc(double StartAnomalyRad, double EndAnomalyRad, ArcEnd End)
{
    public bool IsEmpty => End == ArcEnd.Empty;

    public bool IsClosed => End == ArcEnd.Closed;

    public static ConicArc Empty => new(0, 0, ArcEnd.Empty);

    /// <summary>
    /// The part of <paramref name="conic"/> above <paramref name="surfaceRadiusM"/>; hyperbolic arms are cut where they
    /// pass <paramref name="clipRadiusM"/>.
    /// </summary>
    public static ConicArc Of(in Conic conic, double surfaceRadiusM, double clipRadiusM)
    {
        var p = conic.SemiLatusRectumM;
        if (!double.IsFinite(p) || p <= 0) return Empty;
        var e = conic.Eccentricity;
        var periapsis = p / (1 + e);
        var hitsSurface = periapsis < surfaceRadiusM;
        var surfaceAnomaly = hitsSurface ? conic.TrueAnomalyAtRadius(surfaceRadiusM) : double.NaN;

        if (!conic.IsHyperbolic)
        {
            if (!hitsSurface) return new ConicArc(0, Angles.TwoPi, ArcEnd.Closed);
            if (double.IsNaN(surfaceAnomaly)) return Empty;
            return new ConicArc(surfaceAnomaly, Angles.TwoPi - surfaceAnomaly, ArcEnd.Impact);
        }

        if (periapsis >= clipRadiusM) return Empty;
        var clipAnomaly = conic.TrueAnomalyAtRadius(clipRadiusM);
        if (!hitsSurface) return new ConicArc(-clipAnomaly, clipAnomaly, ArcEnd.ViewEdge);
        return -surfaceAnomaly > -clipAnomaly ? new ConicArc(-clipAnomaly, -surfaceAnomaly, ArcEnd.Impact) : Empty;
    }

    /// <summary>Maps an anomaly in (-π, π] into this arc's continuous range, clamped to its ends.</summary>
    public double Normalize(double anomalyRad)
    {
        if (IsEmpty) return StartAnomalyRad;
        var value = anomalyRad;
        if (EndAnomalyRad > Math.PI && value < StartAnomalyRad) value += Angles.TwoPi;
        return Math.Clamp(value, StartAnomalyRad, EndAnomalyRad);
    }

    /// <summary>True when <paramref name="anomalyRad"/> (any branch) lies on the arc.</summary>
    public bool Contains(double anomalyRad)
    {
        if (IsEmpty) return false;
        if (IsClosed) return true;
        var value = anomalyRad;
        if (EndAnomalyRad > Math.PI && value < StartAnomalyRad) value += Angles.TwoPi;
        return value >= StartAnomalyRad && value <= EndAnomalyRad;
    }
}
