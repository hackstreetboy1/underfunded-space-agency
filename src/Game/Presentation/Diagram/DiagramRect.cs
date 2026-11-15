namespace Usa.Game.Presentation.Diagram;

/// <summary>An axis-aligned rectangle in diagram pixels.</summary>
public readonly record struct DiagramRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public DiagramPoint Centre => new(X + Width / 2, Y + Height / 2);

    public bool Contains(DiagramPoint point) => point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;

    /// <summary>The smallest rectangle around <paramref name="points"/>, grown by <paramref name="padding"/> on every side.</summary>
    public static DiagramRect Around(IEnumerable<DiagramPoint> points, double padding)
    {
        ArgumentNullException.ThrowIfNull(points);
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var point in points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }
        if (double.IsInfinity(minX)) return default;
        return new DiagramRect(minX - padding, minY - padding, maxX - minX + 2 * padding, maxY - minY + 2 * padding);
    }
}
