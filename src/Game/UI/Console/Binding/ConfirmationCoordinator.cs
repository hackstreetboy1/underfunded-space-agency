using Usa.Game.Content;
using Usa.Game.Presentation.Console.Systems;
using Usa.Game.Presentation.Console.Tasks;
using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.UI.Console.Binding;

/// <summary>
/// The console's one high-impact confirmation dialog (UI.md 5.2): a guarded control's change, a comms command, or an unsafe
/// Stop. One prompt at a time: a new request cancels the open one. The countdown runs <see cref="AutoCancelSeconds"/> of real
/// time from the last input; while the game is frozen the prompt cannot be confirmed and does not run out, and the countdown
/// resumes with the time it had left when the freeze ends. Confirm and Cancel go back to whoever asked.
/// </summary>
public sealed class ConfirmationCoordinator : IDisposable
{
    public const double AutoCancelSeconds = 20;

    private readonly SystemsBoardViewModel _board;
    private readonly TaskSlotViewModel _taskSlot;
    private readonly Action _confirmStop;
    private readonly Action _cancelStop;
    private readonly ITextSource? _text;
    private readonly IDisposable _subscription;
    private readonly ObservableValue<ConfirmPrompt?> _prompt = new();
    private readonly ObservableValue<double> _secondsLeft = new(AutoCancelSeconds);
    private readonly ObservableValue<bool> _canConfirm = new(true);
    private Action? _onConfirm;
    private Action? _onCancel;
    private Action? _onTouch;
    private string? _externalId;
    private bool _externalTimeout;
    private bool _frozen;
    private double _elapsedS;

    /// <param name="confirmStop">Sends the pending stop (the bridge's <c>ConfirmStopTask</c>).</param>
    /// <param name="cancelStop">Drops the pending stop (the bridge's <c>CancelStopTask</c>).</param>
    /// <param name="text">Resolves the controls' <c>impact.*</c> effect keys; without it the keys are shown.</param>
    public ConfirmationCoordinator(
        SystemsBoardViewModel board, TaskSlotViewModel taskSlot, Action confirmStop, Action cancelStop, ITextSource? text = null)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(taskSlot);
        ArgumentNullException.ThrowIfNull(confirmStop);
        ArgumentNullException.ThrowIfNull(cancelStop);
        _board = board;
        _taskSlot = taskSlot;
        _confirmStop = confirmStop;
        _cancelStop = cancelStop;
        _text = text;
        _subscription = board.Controls.ConfirmationRequested.Subscribe(ShowControl);
    }

    /// <summary>The open prompt; null when the dialog is closed.</summary>
    public IReadOnlyObservable<ConfirmPrompt?> Prompt => _prompt;

    /// <summary>Seconds left on the countdown, whole and rounded up.</summary>
    public IReadOnlyObservable<double> SecondsLeft => _secondsLeft;

    /// <summary>False while the game is frozen.</summary>
    public IReadOnlyObservable<bool> CanConfirm => _canConfirm;

    /// <summary>The runtime asked to confirm Stop on the running task.</summary>
    public void ShowStop() => Open(
        new ConfirmPrompt(ConfirmPromptKind.StopTask, _taskSlot.TaskName.Value, [], ExecutorName: _taskSlot.AssigneeName.Value),
        _confirmStop, _cancelStop, externalId: null, externalTimeout: false);

    /// <summary>A prompt owned elsewhere, e.g. a comms confirmation that times out on its own.</summary>
    /// <param name="id">Lets the owner close it with <see cref="CloseExternal"/>.</param>
    /// <param name="ownsTimeout">The owner expires it; the dialog only shows the countdown.</param>
    /// <param name="touch">Tells the owner about input on the dialog, so its own timeout restarts too.</param>
    public void ShowExternal(ConfirmPrompt prompt, string id, Action confirm, Action cancel, bool ownsTimeout, Action? touch = null)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(cancel);
        Open(prompt, confirm, cancel, id, ownsTimeout);
        _onTouch = touch;
    }

    /// <summary>The owner closed its prompt (confirmed, cancelled, expired or superseded): hide it without answering.</summary>
    public void CloseExternal(string id)
    {
        if (_prompt.Value is null || !string.Equals(_externalId, id, StringComparison.Ordinal)) return;
        Close();
    }

    /// <returns>False when nothing is open or the game is frozen.</returns>
    public bool Confirm()
    {
        if (_prompt.Value is null || _frozen) return false;
        var confirm = _onConfirm;
        Close();
        confirm?.Invoke();
        return true;
    }

    /// <summary>Cancel, Esc or the timeout.</summary>
    public bool Cancel()
    {
        if (_prompt.Value is null) return false;
        var cancel = _onCancel;
        Close();
        cancel?.Invoke();
        return true;
    }

    /// <summary>Input on the dialog (focus moves, pointer): restarts the no-input countdown.</summary>
    public void Touch()
    {
        if (_prompt.Value is null || _frozen) return;
        ResetCountdown();
        _onTouch?.Invoke();
    }

    public void SetFrozen(bool frozen)
    {
        if (_frozen == frozen) return;
        _frozen = frozen;
        // UI.md 5.2 (decided 2026-11-15): the countdown pauses and then resumes with the time it had left.
        _canConfirm.Value = !frozen;
    }

    /// <summary>Advances the countdown by real seconds; cancels the prompt when it runs out (unless its owner times it).</summary>
    public void Advance(double realSeconds)
    {
        if (_prompt.Value is null || _frozen || !double.IsFinite(realSeconds) || realSeconds <= 0) return;
        _elapsedS = Math.Min(_elapsedS + realSeconds, AutoCancelSeconds);
        _secondsLeft.Value = Math.Ceiling(AutoCancelSeconds - _elapsedS);
        if (_elapsedS >= AutoCancelSeconds && !_externalTimeout) Cancel();
    }

    public void Dispose()
    {
        _subscription.Dispose();
        Cancel();
    }

    private void ShowControl(ConfirmationRequest request)
    {
        var id = request.Intent.ComponentId;
        Open(
            new ConfirmPrompt(
                ConfirmPromptKind.Control, request.Title, ResolveEffects(request.Effects, request.EffectTextKeys, _text), request.Irreversible,
                request.ExecutorName),
            () => _board.FindControl(id)?.Confirm(),
            () => _board.FindControl(id)?.Cancel(),
            externalId: null, externalTimeout: false);
    }

    /// <summary>
    /// The effects a prompt lists: the texts already in words, then each <c>impact.*</c> key resolved. A missing key must not
    /// block a safety prompt, so it is shown as the key rather than dropped.
    /// </summary>
    public static IReadOnlyList<string> ResolveEffects(IReadOnlyList<string> texts, IReadOnlyList<string> keys, ITextSource? text)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0) return texts;
        var effects = new List<string>(texts.Count + keys.Count);
        effects.AddRange(texts);
        foreach (var key in keys)
        {
            effects.Add(Resolve(key, text));
        }
        return effects;
    }

    private static string Resolve(string key, ITextSource? text)
    {
        if (text is null) return key;
        try
        {
            return text.Get(key);
        }
        catch (KeyNotFoundException)
        {
            return key;
        }
    }

    private void Open(ConfirmPrompt prompt, Action confirm, Action cancel, string? externalId, bool externalTimeout)
    {
        Cancel();
        _onConfirm = confirm;
        _onCancel = cancel;
        _externalId = externalId;
        _externalTimeout = externalTimeout;
        ResetCountdown();
        _prompt.Value = prompt;
    }

    private void Close()
    {
        _onConfirm = null;
        _onCancel = null;
        _onTouch = null;
        _externalId = null;
        _externalTimeout = false;
        _prompt.Value = null;
    }

    private void ResetCountdown()
    {
        _elapsedS = 0;
        _secondsLeft.Value = AutoCancelSeconds;
    }
}
