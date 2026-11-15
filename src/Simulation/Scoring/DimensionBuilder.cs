namespace Usa.Simulation.Scoring;

/// <summary>Accumulates weighted criterion lines into a <see cref="DimensionScore"/>.</summary>
internal sealed class DimensionBuilder(ScoreDimension dimension, double weightPoints)
{
    private readonly List<ScoreLine> _lines = [];
    private readonly List<ScoreNote> _notes = [];
    private double _points;

    public double WeightPoints => weightPoints;

    /// <param name="subWeight">Share of the dimension (0-1).</param>
    /// <param name="credit">Fraction of that share earned (0-1).</param>
    public void Line(string key, double subWeight, double credit, string? subject = null) =>
        Points(key, weightPoints * subWeight * Ramp.Clamp01(credit), weightPoints * subWeight, subject);

    public void Points(string key, double points, double maxPoints, string? subject = null)
    {
        _lines.Add(new ScoreLine(key, subject, points, maxPoints));
        _points += points;
    }

    public void Note(string key, string? subject = null, double? value = null) => _notes.Add(new ScoreNote(key, subject, value));

    public DimensionScore Build() => new(dimension, weightPoints, Math.Min(_points, weightPoints), _lines, _notes);
}
