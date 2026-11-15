using Usa.Game.Presentation.Map;

namespace Usa.Game.UI.Map.Geometry;

/// <summary>
/// Reusable storage for several polylines (or polygons) in one flat point buffer: run <c>i</c> is
/// <see cref="Points"/>[<see cref="Start"/>(i) .. <see cref="Start"/>(i) + <see cref="Length"/>(i)]. <see cref="Clear"/>
/// keeps the capacity, so rebuilding every frame does not allocate.
/// </summary>
public sealed class PolylineSet
{
    private readonly List<MapPoint> _points = [];
    private readonly List<int> _starts = [];
    private int _open = -1;

    public IReadOnlyList<MapPoint> Points => _points;

    public int Count => _starts.Count;

    public int PointCount => _points.Count;

    /// <summary>Points in the largest run.</summary>
    public int MaxRunLength
    {
        get
        {
            var max = 0;
            for (var i = 0; i < Count; i++) max = Math.Max(max, Length(i));
            return max;
        }
    }

    public int Start(int run) => _starts[run];

    public int Length(int run) => (run + 1 < _starts.Count ? _starts[run + 1] : _points.Count) - _starts[run];

    public void Clear()
    {
        _points.Clear();
        _starts.Clear();
        _open = -1;
    }

    /// <summary>Starts a new run; an open run with fewer than <paramref name="minPoints"/> points is dropped first.</summary>
    public void Begin(int minPoints = 2)
    {
        End(minPoints);
        _open = _points.Count;
        _starts.Add(_open);
    }

    public void Add(MapPoint point) => _points.Add(point);

    /// <summary>Closes the open run, dropping it when it has fewer than <paramref name="minPoints"/> points.</summary>
    public void End(int minPoints = 2)
    {
        if (_open < 0) return;
        if (_points.Count - _open < minPoints)
        {
            _points.RemoveRange(_open, _points.Count - _open);
            _starts.RemoveAt(_starts.Count - 1);
        }
        _open = -1;
    }

    public bool IsOpen => _open >= 0;

    public int OpenLength => _open < 0 ? 0 : _points.Count - _open;

    public MapPoint Last => _points[^1];
}
