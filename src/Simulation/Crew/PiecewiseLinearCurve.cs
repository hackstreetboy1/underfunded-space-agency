using System.Globalization;
using Usa.Simulation.Data;

namespace Usa.Simulation.Crew;

/// <summary>Linear interpolation between points with strictly increasing x; flat beyond the first and last point.</summary>
public sealed class PiecewiseLinearCurve
{
    private readonly double[] _x;
    private readonly double[] _y;

    private PiecewiseLinearCurve(double[] x, double[] y)
    {
        _x = x;
        _y = y;
    }

    public double MinX => _x[0];

    public double MaxX => _x[^1];

    public double Evaluate(double x)
    {
        if (!(x > _x[0])) return _y[0];
        for (var i = 1; i < _x.Length; i++)
        {
            if (x <= _x[i])
            {
                var t = (x - _x[i - 1]) / (_x[i] - _x[i - 1]);
                return _y[i - 1] + t * (_y[i] - _y[i - 1]);
            }
        }
        return _y[^1];
    }

    /// <summary>Builds a curve from JSON [x, y] pairs, adding every problem to <paramref name="errors"/>.</summary>
    /// <returns>The curve, or null when the points are invalid.</returns>
    public static PiecewiseLinearCurve? Create(
        IReadOnlyList<IReadOnlyList<double>> points, string file, string jsonPath, DefinitionErrors errors, double minY = double.NegativeInfinity)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(errors);
        if (points.Count == 0)
        {
            errors.Add(file, jsonPath, "A curve needs at least one [x, y] point.");
            return null;
        }

        var x = new double[points.Count];
        var y = new double[points.Count];
        var valid = true;
        for (var i = 0; i < points.Count; i++)
        {
            var path = string.Create(CultureInfo.InvariantCulture, $"{jsonPath}[{i}]");
            if (points[i].Count != 2)
            {
                errors.Add(file, path, "Each point is [x, y].");
                valid = false;
                continue;
            }
            x[i] = points[i][0];
            y[i] = points[i][1];
            if (!double.IsFinite(x[i]) || !double.IsFinite(y[i]))
            {
                errors.Add(file, path, "Point values must be finite.");
                valid = false;
            }
            else if (y[i] < minY)
            {
                errors.Add(file, path, string.Create(CultureInfo.InvariantCulture, $"y must be at least {minY}."));
                valid = false;
            }
            if (i > 0 && !(x[i] > x[i - 1]))
            {
                errors.Add(file, path, "x values must increase strictly.");
                valid = false;
            }
        }
        return valid ? new PiecewiseLinearCurve(x, y) : null;
    }
}
