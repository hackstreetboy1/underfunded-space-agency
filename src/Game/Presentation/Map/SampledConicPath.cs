using Usa.Simulation.Flight;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Game.Presentation.Map;

/// <summary>World-space samples of one conic arc, ordered by true anomaly (continuous over <see cref="Arc"/>).</summary>
public sealed class SampledConicPath
{
    private readonly double[] _anomalies;
    private readonly Vec2d[] _pointsM;

    internal SampledConicPath(Conic conic, ConicArc arc, double toleranceM, double[] anomalies, Vec2d[] pointsM)
    {
        Conic = conic;
        Arc = arc;
        ToleranceM = toleranceM;
        _anomalies = anomalies;
        _pointsM = pointsM;
    }

    public Conic Conic { get; }

    public ConicArc Arc { get; }

    /// <summary>Largest distance between a chord and the conic.</summary>
    public double ToleranceM { get; }

    public IReadOnlyList<double> AnomaliesRad => _anomalies;

    public IReadOnlyList<Vec2d> PointsM => _pointsM;

    public int Count => _pointsM.Length;

    /// <summary>The samples after <paramref name="anomalyRad"/>, starting with the exact point there.</summary>
    public IReadOnlyList<Vec2d> From(double anomalyRad)
    {
        if (_pointsM.Length == 0) return [];
        var start = Arc.Normalize(anomalyRad);
        var first = Array.BinarySearch(_anomalies, start);
        first = first >= 0 ? first + 1 : ~first;
        var result = new List<Vec2d>(_pointsM.Length - first + 1) { ConicGeometry.PositionAt(Conic, start) };
        for (var i = first; i < _pointsM.Length; i++) result.Add(_pointsM[i]);
        return result;
    }

    /// <summary>The samples between two anomalies on the arc, both ends exact.</summary>
    public IReadOnlyList<Vec2d> Between(double fromAnomalyRad, double toAnomalyRad)
    {
        if (_pointsM.Length == 0) return [];
        var start = Arc.Normalize(fromAnomalyRad);
        var end = Arc.Normalize(toAnomalyRad);
        if (end < start) end = start;
        var result = new List<Vec2d> { ConicGeometry.PositionAt(Conic, start) };
        for (var i = 0; i < _pointsM.Length; i++)
        {
            if (_anomalies[i] > start && _anomalies[i] < end) result.Add(_pointsM[i]);
        }
        result.Add(ConicGeometry.PositionAt(Conic, end));
        return result;
    }
}
