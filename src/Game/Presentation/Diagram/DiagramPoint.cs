namespace Usa.Game.Presentation.Diagram;

/// <summary>A point in diagram layout pixels (the vehicle JSON <c>diagram</c> coordinates; Y down).</summary>
public readonly record struct DiagramPoint(double X, double Y)
{
    public double DistanceTo(DiagramPoint other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
