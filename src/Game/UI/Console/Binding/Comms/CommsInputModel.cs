using Usa.Game.Integration.Adapters;
using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.UI.Console.Binding.Comms;

/// <summary>What pressing Send does with the input text.</summary>
public enum CommsSendRoute
{
    /// <summary>Nothing to send (blank input).</summary>
    None,

    /// <summary>Free text: the crew interpret it (<c>CommsController.SendText</c>).</summary>
    Text,

    /// <summary>
    /// The unchanged text of an "Ask" or "Request…" pre-fill that maps to one action: send its suggested request directly, with no
    /// interpretation (<see cref="CommsPrefill.Suggested"/>).
    /// </summary>
    Suggested,
}

/// <summary>
/// The comms input's Godot-free state (UI.md 3.5): the pending pre-fill and its suggested request, and the "Undo (2 s)" chip
/// offered after a voice order was sent without review. The panel shows what this exposes and forwards the player's input.
/// </summary>
public sealed class CommsInputModel
{
    /// <summary>How long a voice order can be taken back after it was sent (UI.md 3.5).</summary>
    public const double UndoSeconds = 2;

    private readonly ObservableValue<int> _undoSecondsLeft = new();
    private CommsPrefill? _prefill;
    private (string RequestId, string Text)? _undo;
    private double _undoElapsedS;

    /// <summary>The pre-fill waiting in the input; null when the player typed over it or sent it.</summary>
    public CommsPrefill? Prefill => _prefill;

    /// <summary>Whole seconds left on the undo chip (rounded up); 0 hides it.</summary>
    public IReadOnlyObservable<int> UndoSecondsLeft => _undoSecondsLeft;

    /// <summary>The request the undo chip would cancel; null when none is offered.</summary>
    public string? UndoRequestId => _undo?.RequestId;

    /// <summary>"Ask" or "Request…" placed text in the input.</summary>
    public void SetPrefill(CommsPrefill prefill)
    {
        ArgumentNullException.ThrowIfNull(prefill);
        _prefill = prefill;
    }

    /// <summary>The player edited the input: a pre-fill whose text no longer matches is dropped.</summary>
    public void TextChanged(string text)
    {
        if (_prefill is { } prefill && !SameText(prefill.Text, text)) _prefill = null;
    }

    /// <summary>Where Send takes <paramref name="text"/>; <paramref name="suggested"/> is the request for <see cref="CommsSendRoute.Suggested"/>.</summary>
    public CommsSendRoute Route(string text, out CommandDraft? suggested)
    {
        suggested = null;
        if (string.IsNullOrWhiteSpace(text)) return CommsSendRoute.None;
        if (_prefill is { Suggested: { } draft } prefill && SameText(prefill.Text, text))
        {
            suggested = draft;
            return CommsSendRoute.Suggested;
        }
        return CommsSendRoute.Text;
    }

    /// <summary>The input was sent; the pre-fill is used up.</summary>
    public void Sent() => _prefill = null;

    /// <summary>A voice order went out without review: offer to take it back for <see cref="UndoSeconds"/>.</summary>
    public void OfferUndo(string requestId, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(text);
        _undo = (requestId, text);
        _undoElapsedS = 0;
        _undoSecondsLeft.Value = (int)UndoSeconds;
    }

    /// <summary>Real time for the undo chip; it disappears when the time is up.</summary>
    public void Advance(double realSeconds)
    {
        if (_undo is null || !double.IsFinite(realSeconds) || realSeconds <= 0) return;
        _undoElapsedS += realSeconds;
        if (_undoElapsedS >= UndoSeconds)
        {
            ClearUndo();
            return;
        }
        _undoSecondsLeft.Value = (int)Math.Ceiling(UndoSeconds - _undoElapsedS);
    }

    /// <summary>The undo chip was pressed: the order to cancel and the text to put back in the input.</summary>
    /// <returns>False when the chip has gone.</returns>
    public bool TryUndo(out string requestId, out string text)
    {
        if (_undo is not { } undo)
        {
            requestId = string.Empty;
            text = string.Empty;
            return false;
        }
        (requestId, text) = undo;
        ClearUndo();
        return true;
    }

    public void ClearUndo()
    {
        _undo = null;
        _undoElapsedS = 0;
        _undoSecondsLeft.Value = 0;
    }

    private static bool SameText(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.Ordinal);
}
