namespace Usa.Game.Comms;

/// <summary>
/// Decides which started speech lines get a caption and for how long. Only lines this run delivered are captioned, each
/// once (a line queued by an earlier run or another source never writes into this run's panel). The caption stays up for
/// the speech player's hold for that line (clip or reading time plus the tail), never less than
/// <see cref="MinCaptionSeconds"/> so a bad read cannot flash a caption away.
/// </summary>
/// <typeparam name="TLine">The player's line type; lines are matched by reference.</typeparam>
public sealed class CaptionGate<TLine>
    where TLine : class
{
    /// <summary>UI.md's shortest line (1.5 s) plus its 1.5 s tail.</summary>
    public const double MinCaptionSeconds = 3.0;

    private readonly HashSet<TLine> _delivered = new(ReferenceEqualityComparer.Instance);

    public int PendingCount => _delivered.Count;

    public void Track(TLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        _delivered.Add(line);
    }

    /// <summary>Call when <paramref name="line"/> starts playing, with the player's hold for it.</summary>
    /// <returns>False when the line was not delivered here (or already started): show no caption.</returns>
    public bool TryStart(TLine line, double holdSeconds, out double captionSeconds)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!_delivered.Remove(line))
        {
            captionSeconds = 0;
            return false;
        }

        captionSeconds = double.IsFinite(holdSeconds) ? Math.Max(holdSeconds, MinCaptionSeconds) : MinCaptionSeconds;
        return true;
    }

    public void Clear() => _delivered.Clear();
}
