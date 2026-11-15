namespace Usa.Simulation.Scoring;

/// <summary>Maps a score to a grade (GDD 10.2), applying the S+ gate and the death S-tier block.</summary>
public sealed class GradeTable
{
    private readonly GradeBand[] _bands;

    /// <param name="bands">Bands lowest first, starting at 0 and rising strictly (see <see cref="ScoringConfigLoader"/>).</param>
    public GradeTable(IReadOnlyList<GradeBand> bands)
    {
        ArgumentNullException.ThrowIfNull(bands);
        if (bands.Count == 0) throw new ArgumentException("At least one band is required.", nameof(bands));
        if (bands[0].MinPoints != 0) throw new ArgumentException("The lowest band must start at 0.", nameof(bands));
        for (var i = 1; i < bands.Count; i++)
        {
            if (bands[i].MinPoints <= bands[i - 1].MinPoints)
                throw new ArgumentException($"Band '{bands[i].Label}' does not rise above '{bands[i - 1].Label}'.", nameof(bands));
        }
        _bands = [.. bands];
    }

    public IReadOnlyList<GradeBand> Bands => _bands;

    /// <param name="score">The exact, unrounded score; 97.5 is below a band starting at 98.</param>
    /// <param name="sPlusEligible">The S+ gate passed; without it the S+ band grades as S.</param>
    /// <param name="anyCrewLost">Any death blocks every S tier (GDD D11).</param>
    public Grade Lookup(double score, bool sPlusEligible, bool anyCrewLost)
    {
        if (double.IsNaN(score)) throw new ArgumentOutOfRangeException(nameof(score), score, "Score is not a number.");
        var grade = _bands[0].Grade;
        foreach (var band in _bands)
        {
            if (score >= band.MinPoints) grade = band.Grade;
        }

        if (grade == Grade.SPlus && !sPlusEligible) grade = Grade.S;
        if (anyCrewLost && grade >= Grade.SMinus) grade = Grade.APlus;
        return grade;
    }

    public string Label(Grade grade)
    {
        foreach (var band in _bands)
        {
            if (band.Grade == grade) return band.Label;
        }
        return grade.ToString();
    }
}
