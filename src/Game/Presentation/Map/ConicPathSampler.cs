using Usa.Simulation.Flight;
using Usa.Simulation.Kernel.Geometry;

namespace Usa.Game.Presentation.Map;

/// <summary>
/// Turns conic arcs into world-space polylines whose chords stay within a distance tolerance of the curve:
/// segments are split at their midpoint anomaly until the sagitta (≈ L²κ/8) is small enough, so tight
/// periapsis turns get dense points and flat stretches few. Results are cached until the conic, arc or tolerance
/// bucket changes; tolerances are rounded down to powers of two so small zoom steps reuse the samples.
/// </summary>
public sealed class ConicPathSampler
{
    private const int MaxDepth = 18;
    private const int MaxPoints = 8192;
    private const double InitialStepRad = Math.PI / 16;

    private readonly int _capacity;
    private readonly List<SampledConicPath> _cache;

    /// <param name="capacity">Distinct paths kept (current orbit, ghost, corridor limits...).</param>
    public ConicPathSampler(int capacity = 8)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _cache = new List<SampledConicPath>(capacity);
    }

    /// <summary>How many times sampling actually ran (cache misses), for tests and diagnostics.</summary>
    public int SampleCount { get; private set; }

    /// <summary>The largest power of two not above <paramref name="toleranceM"/>.</summary>
    public static double BucketTolerance(double toleranceM)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(toleranceM);
        return Math.Pow(2, Math.Floor(Math.Log2(toleranceM)));
    }

    public SampledConicPath Sample(in Conic conic, ConicArc arc, double toleranceM)
    {
        var bucket = BucketTolerance(toleranceM);
        for (var i = 0; i < _cache.Count; i++)
        {
            var cached = _cache[i];
            if (cached.Conic == conic && cached.Arc == arc && cached.ToleranceM == bucket)
            {
                if (i > 0)
                {
                    _cache.RemoveAt(i);
                    _cache.Insert(0, cached);
                }
                return cached;
            }
        }

        var path = SampleUncached(conic, arc, bucket);
        SampleCount++;
        if (_cache.Count == _capacity) _cache.RemoveAt(_cache.Count - 1);
        _cache.Insert(0, path);
        return path;
    }

    public void Clear() => _cache.Clear();

    private static SampledConicPath SampleUncached(Conic conic, ConicArc arc, double toleranceM)
    {
        if (arc.IsEmpty) return new SampledConicPath(conic, arc, toleranceM, [], []);

        var anomalies = new List<double>(256);
        var points = new List<Vec2d>(256);
        var start = arc.StartAnomalyRad;
        var span = arc.EndAnomalyRad - start;
        var segments = Math.Max(8, (int)Math.Ceiling(span / InitialStepRad));
        var previousAnomaly = start;
        var previousPoint = ConicGeometry.PositionAt(conic, start);
        anomalies.Add(previousAnomaly);
        points.Add(previousPoint);
        for (var k = 1; k <= segments; k++)
        {
            var anomaly = k == segments ? arc.EndAnomalyRad : start + span * k / segments;
            var point = ConicGeometry.PositionAt(conic, anomaly);
            Subdivide(conic, toleranceM, previousAnomaly, previousPoint, anomaly, point, 0, anomalies, points);
            previousAnomaly = anomaly;
            previousPoint = point;
        }
        return new SampledConicPath(conic, arc, toleranceM, [.. anomalies], [.. points]);
    }

    /// <summary>Appends the samples after (<paramref name="a0"/>, <paramref name="p0"/>) up to and including <paramref name="a1"/>.</summary>
    private static void Subdivide(
        in Conic conic, double toleranceM, double a0, Vec2d p0, double a1, Vec2d p1, int depth,
        List<double> anomalies, List<Vec2d> points)
    {
        var midAnomaly = (a0 + a1) / 2;
        var mid = ConicGeometry.PositionAt(conic, midAnomaly);
        var sagitta = (mid - (p0 + p1) / 2).Length;
        if (sagitta > toleranceM && depth < MaxDepth && points.Count < MaxPoints)
        {
            Subdivide(conic, toleranceM, a0, p0, midAnomaly, mid, depth + 1, anomalies, points);
            Subdivide(conic, toleranceM, midAnomaly, mid, a1, p1, depth + 1, anomalies, points);
            return;
        }
        anomalies.Add(a1);
        points.Add(p1);
    }
}
