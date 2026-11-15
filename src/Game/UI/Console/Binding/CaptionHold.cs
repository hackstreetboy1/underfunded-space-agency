using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.UI.Console.Binding;

/// <summary>A caption on screen: who speaks and what (UI.md 2.4 Caption, 7: captions for every spoken line).</summary>
public sealed record CaptionLine(string Speaker, string Text);

/// <summary>
/// Keeps the current crew caption up for its hold time (clip or reading time plus the 1.5 s tail, from the comms
/// controller). A new line replaces the current one; time spent frozen does not count.
/// </summary>
public sealed class CaptionHold
{
    private readonly ObservableValue<CaptionLine?> _current = new();
    private double _remainingS;
    private bool _frozen;

    /// <summary>The caption to show; null when none.</summary>
    public IReadOnlyObservable<CaptionLine?> Current => _current;

    public void Show(string speaker, string text, double holdSeconds)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text) || !double.IsFinite(holdSeconds) || holdSeconds <= 0)
        {
            Clear();
            return;
        }
        _remainingS = holdSeconds;
        _current.Value = new CaptionLine(speaker, text);
    }

    public void SetFrozen(bool frozen) => _frozen = frozen;

    /// <summary>Counts real seconds down while not frozen; clears the caption when its hold ends.</summary>
    public void Advance(double realSeconds)
    {
        if (_current.Value is null || _frozen || !double.IsFinite(realSeconds) || realSeconds <= 0) return;
        _remainingS -= realSeconds;
        if (_remainingS <= 0) Clear();
    }

    public void Clear()
    {
        _remainingS = 0;
        _current.Value = null;
    }
}
